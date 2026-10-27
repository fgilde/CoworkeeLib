using Coworkee.Application.Messaging;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

public sealed class OutboxTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly RecordingHandler _handler = new();

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Domain_events_are_stored_and_dispatched()
    {
        await using var provider = Services();
        var document = new Document { Title = "Draft" };
        document.Rename("Final");
        await SaveAsync(provider, document);

        var processed = await provider.GetRequiredService<OutboxProcessor<TestDbContext>>().ProcessBatchAsync(10, TestContext.Current.CancellationToken);

        processed.ShouldBe(1);
        _handler.Received.ShouldHaveSingleItem().Title.ShouldBe("Final");
        (await MessagesAsync(provider)).Single().ProcessedAt.ShouldBe(_clock.GetUtcNow());
        document.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Failing_message_is_marked_and_does_not_block_others()
    {
        await using var provider = Services();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            db.Set<OutboxMessage>().Add(new OutboxMessage { Type = "Unknown.Type, Missing", Payload = "{}", OccurredAt = _clock.GetUtcNow() });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var document = new Document { Title = "A" };
        document.Rename("B");
        await SaveAsync(provider, document);

        await provider.GetRequiredService<OutboxProcessor<TestDbContext>>().ProcessBatchAsync(10, TestContext.Current.CancellationToken);

        var messages = await MessagesAsync(provider);
        messages.Single(m => m.Type.StartsWith("Unknown", StringComparison.Ordinal)).ShouldSatisfyAllConditions(
            m => m.ProcessedAt.ShouldBeNull(),
            m => m.Attempts.ShouldBe(1),
            m => m.Error.ShouldNotBeNullOrEmpty());
        _handler.Received.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Failed_message_waits_for_backoff_before_retry()
    {
        await using var provider = Services();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            db.Set<OutboxMessage>().Add(new OutboxMessage { Type = "Unknown.Type, Missing", Payload = "{}", OccurredAt = _clock.GetUtcNow() });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var processor = provider.GetRequiredService<OutboxProcessor<TestDbContext>>();
        await processor.ProcessBatchAsync(10, TestContext.Current.CancellationToken);
        (await processor.ProcessBatchAsync(10, TestContext.Current.CancellationToken)).ShouldBe(0);

        _clock.Advance(TimeSpan.FromMinutes(1));
        (await processor.ProcessBatchAsync(10, TestContext.Current.CancellationToken)).ShouldBe(1);
        (await MessagesAsync(provider)).Single().Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Handlers_run_as_the_user_and_tenant_that_raised_the_event()
    {
        var context = new ContextRecorder();
        await using var provider = Services(services =>
        {
            services.AddSingleton(context);
            services.AddScoped<IDomainEventHandler<DocumentRenamed>, ContextRecordingHandler>();
        });
        var raisingUser = _user.UserId;
        var raisingTenant = _user.TenantId;
        var document = new Document { Title = "A" };
        document.Rename("B");
        await SaveAsync(provider, document);

        _user.UserId = null;
        _user.TenantId = null;
        await provider.GetRequiredService<OutboxProcessor<TestDbContext>>().ProcessBatchAsync(10, TestContext.Current.CancellationToken);

        context.UserId.ShouldBe(raisingUser);
        context.TenantId.ShouldBe(raisingTenant);
    }

    [Fact]
    public async Task Handlers_of_an_event_raised_while_signed_in_as_another_user_name_the_impersonator_in_the_audit_trail()
    {
        await using var provider = Services(services => services.AddScoped<IDomainEventHandler<DocumentRenamed>, CopyingHandler>());
        _user.ImpersonatorId = Guid.CreateVersion7();
        var (raisingUser, impersonator) = (_user.UserId, _user.ImpersonatorId);
        var document = new Document { Title = "A" };
        document.Rename("B");
        await SaveAsync(provider, document);

        _user.UserId = null;
        _user.ImpersonatorId = null;
        await provider.GetRequiredService<OutboxProcessor<TestDbContext>>().ProcessBatchAsync(10, TestContext.Current.CancellationToken);

        (await MessagesAsync(provider)).Single().ImpersonatorId.ShouldBe(impersonator);
        await using var scope = provider.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<TestDbContext>().Set<Auditing.AuditEntry>().IgnoreQueryFilters()
            .Select(e => new { e.ActorId, e.ImpersonatorId }).ToListAsync(TestContext.Current.CancellationToken);
        entries.Count.ShouldBe(2);
        entries.ShouldAllBe(e => e.ActorId == raisingUser && e.ImpersonatorId == impersonator);
    }

    private ServiceProvider Services(Action<IServiceCollection>? configure = null) => database.CreateServices(_user, _clock, services =>
    {
        services.AddSingleton<IDomainEventHandler<DocumentRenamed>>(_handler);
        services.AddCoworkeeOutboxProcessing<TestDbContext>();
        configure?.Invoke(services);
    });

    private sealed class ContextRecorder
    {
        public Guid? UserId { get; set; }

        public Guid? TenantId { get; set; }
    }

    private sealed class ContextRecordingHandler(ICurrentUser currentUser, ContextRecorder recorder) : IDomainEventHandler<DocumentRenamed>
    {
        public Task HandleAsync(DocumentRenamed domainEvent, CancellationToken cancellationToken)
        {
            recorder.UserId = currentUser.UserId;
            recorder.TenantId = currentUser.TenantId;
            return Task.CompletedTask;
        }
    }

    private sealed class CopyingHandler(TestDbContext db) : IDomainEventHandler<DocumentRenamed>
    {
        public async Task HandleAsync(DocumentRenamed domainEvent, CancellationToken cancellationToken)
        {
            db.Documents.Add(new Document { Title = "Copy of " + domainEvent.Title });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SaveAsync(IServiceProvider provider, Document document)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        db.Documents.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<OutboxMessage>> MessagesAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Set<OutboxMessage>().ToListAsync(TestContext.Current.CancellationToken);
    }

    private sealed class RecordingHandler : IDomainEventHandler<DocumentRenamed>
    {
        public List<DocumentRenamed> Received { get; } = [];

        public Task HandleAsync(DocumentRenamed domainEvent, CancellationToken cancellationToken)
        {
            Received.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
