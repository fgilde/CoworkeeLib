using Coworkee.Application.Messaging;
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

    private ServiceProvider Services() => database.CreateServices(_user, _clock, services =>
    {
        services.AddSingleton<IDomainEventHandler<DocumentRenamed>>(_handler);
        services.AddCoworkeeOutboxProcessing<TestDbContext>();
    });

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
