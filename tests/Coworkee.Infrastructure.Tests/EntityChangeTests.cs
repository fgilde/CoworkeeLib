using System.Collections.Concurrent;
using System.Diagnostics;
using Coworkee.Application.Events;
using Coworkee.Application.Messaging;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Changes;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

public sealed class EntityChangeTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new() { ImpersonatorId = Guid.CreateVersion7() };
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly RecordingSink _first = new(), _second = new();

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_scan_hands_every_sink_the_same_changes_with_who_when_why_and_masked_values()
    {
        await using var provider = Services();
        using var activity = new Activity("request").Start();
        var document = new Document { Title = "Doc", Secret = "s3cret" };
        await SaveAsync(provider, db => db.Add(document));
        await SaveAsync(provider, async db => (await db.Documents.SingleAsync(d => d.Id == document.Id, Ct)).Title = "Doc 2");
        await SaveAsync(provider, async db => db.Remove(await db.Documents.SingleAsync(d => d.Id == document.Id, Ct)));
        await SaveAsync(provider, async db => (await db.Documents.IgnoreQueryFilters().SingleAsync(d => d.Id == document.Id, Ct)).IsDeleted = false);

        var changes = _first.Committed.Where(c => c.EntityType == nameof(Document)).ToList();
        _second.Committed.Where(c => c.EntityType == nameof(Document)).ShouldBe(changes);
        changes.Select(c => c.Action).ShouldBe([ChangeAction.Created, ChangeAction.Updated, ChangeAction.Deleted, ChangeAction.Restored]);
        changes.ShouldAllBe(c => c.EntityId == document.Id.ToString() && c.TenantId == _user.TenantId && c.CorrelationId == activity.TraceId.ToHexString());
        changes[0].Actor.ShouldBe(new Contracts.Realtime.ActorInfo(_user.UserId, null, _user.ImpersonatorId));
        changes[0].At.ShouldBe(_clock.GetUtcNow());
        changes[0].Changes.ShouldContain(new PropertyChange(nameof(Document.Secret), null, "\"***\""));
        changes[1].Changes.ShouldBe([new PropertyChange(nameof(Document.Title), "\"Doc\"", "\"Doc 2\"")]);
        _first.Scans.ShouldBe(4);
    }

    [Fact]
    public async Task A_rolled_back_transaction_commits_nothing_to_the_sinks()
    {
        await using var provider = Services();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            db.Add(new Document { Title = "Gone" });
            await db.SaveChangesAsync(Ct);
            _first.Committed.ShouldBeEmpty();
            await transaction.RollbackAsync(Ct);
        }

        _first.Committed.ShouldBeEmpty();
        _first.Discards.ShouldBe(1);
    }

    [Fact]
    public async Task A_reason_scope_lands_on_the_changes_and_the_audit_trail()
    {
        await using var provider = Services();
        var document = new Document { Title = "Imported" };
        using (ChangeReason.Begin("Import from CW1"))
        {
            await SaveAsync(provider, db => db.Add(document));
        }

        await SaveAsync(provider, async db => (await db.Documents.SingleAsync(d => d.Id == document.Id, Ct)).Title = "Edited");

        _first.Committed.Where(c => c.EntityType == nameof(Document)).Select(c => c.Reason).ShouldBe(["Import from CW1", null]);
        await using var scope = provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TestDbContext>().Set<AuditEntry>().OrderBy(e => e.OccurredAt).ThenBy(e => e.Action)
            .Select(e => e.Reason).ToListAsync(Ct)).ShouldBe(["Import from CW1", null]);
    }

    [Fact]
    public async Task Only_published_types_write_outbox_rows_by_options_attribute_or_base_type()
    {
        await using var provider = Services(services => services.AddCoworkeeEvents(e => e.PublishChanges<Article>()));
        await SaveAsync(provider, db => db.AddRange(new Article { Title = "A" }, new Parcel { Code = "P1" }, new ExpressParcel { Code = "P2" }, new Document { Title = "D" }));

        var rows = await RowsAsync(provider);
        rows.Select(r => r.Type).Order(StringComparer.Ordinal).ShouldBe(
            [typeof(EntityChange<Article>).AssemblyQualifiedName!, typeof(EntityChange<Parcel>).AssemblyQualifiedName!, typeof(EntityChange<Parcel>).AssemblyQualifiedName!],
            ignoreOrder: true);
        rows.ShouldAllBe(r => r.ActorId == _user.UserId && r.ImpersonatorId == _user.ImpersonatorId && r.TenantId == _user.TenantId && r.OccurredAt == _clock.GetUtcNow());
    }

    [Fact]
    public async Task A_change_handler_runs_once_after_the_commit_as_the_user_with_the_change_and_is_retried_when_it_fails()
    {
        var handler = new ParcelHandler { FailFirst = true };
        await using var provider = Services(services =>
        {
            services.AddSingleton(handler);
            services.AddScoped<IDomainEventHandler<EntityChange<Parcel>>>(sp => new ScopedParcelHandler(sp.GetRequiredService<ParcelHandler>(), sp.GetRequiredService<ICurrentUser>()));
            services.AddCoworkeeOutboxProcessing<TestDbContext>();
        });
        var parcel = new Parcel { Code = "P1" };
        using (ChangeReason.Begin("Booked"))
        {
            await SaveAsync(provider, db => db.Add(parcel));
        }

        var (user, tenant) = (_user.UserId, _user.TenantId);
        _user.UserId = null;
        _user.TenantId = null;
        var processor = provider.GetRequiredService<OutboxProcessor<TestDbContext>>();
        await processor.ProcessBatchAsync(10, Ct);
        handler.Received.ShouldBeEmpty();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await processor.ProcessBatchAsync(10, Ct);
        await processor.ProcessBatchAsync(10, Ct);

        var change = handler.Received.ShouldHaveSingleItem();
        (change.Change.EntityType, change.Change.EntityId, change.Change.Action, change.Change.Reason).ShouldBe((nameof(Parcel), parcel.Id.ToString(), ChangeAction.Created, "Booked"));
        change.Change.Changes.ShouldContain(new PropertyChange(nameof(Parcel.Code), null, "\"P1\""));
        (change.User, change.Tenant).ShouldBe((user, tenant));
    }

    [Fact]
    public async Task Runtime_subscribers_hear_changes_after_the_commit_typed_or_all_and_stop_when_disposed()
    {
        await using var provider = Services();
        var events = provider.GetRequiredService<IEntityEvents>();
        var parcels = new ConcurrentQueue<EntityChange<Parcel>>();
        var all = new ConcurrentQueue<EntityChange>();
        using var typed = events.Subscribe<EntityChange<Parcel>>((c, _) =>
        {
            parcels.Enqueue(c);
            return Task.CompletedTask;
        });
        var everything = events.Subscribe<EntityChange>((c, _) =>
        {
            all.Enqueue(c);
            return Task.CompletedTask;
        });
        using var failing = events.Subscribe<EntityChange>((_, _) => throw new InvalidOperationException("broken subscriber"));

        await SaveAsync(provider, db => db.AddRange(new ExpressParcel { Code = "P1" }, new Document { Title = "D" }));
        parcels.ShouldHaveSingleItem().Action.ShouldBe(ChangeAction.Created);
        all.Select(c => c.EntityType).ShouldContain(nameof(Document));

        everything.Dispose();
        await SaveAsync(provider, db => db.Add(new Document { Title = "E" }));
        all.Count(c => c.EntityType == nameof(Document)).ShouldBe(1);
    }

    private ServiceProvider Services(Action<IServiceCollection>? configure = null) => database.CreateServices(_user, _clock, services =>
    {
        services.AddScoped<IEntityChangeSink>(_ => _first);
        services.AddScoped<IEntityChangeSink>(_ => _second);
        configure?.Invoke(services);
    });

    private static Task SaveAsync(IServiceProvider provider, Action<TestDbContext> change) => SaveAsync(provider, db =>
    {
        change(db);
        return Task.CompletedTask;
    });

    private static async Task SaveAsync(IServiceProvider provider, Func<TestDbContext, Task> change)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await change(db);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<List<OutboxMessage>> RowsAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Set<OutboxMessage>().Where(m => m.Type.StartsWith("Coworkee.Application.Events.EntityChange")).ToListAsync(Ct);
    }

    /// <summary>Records what it is handed, per save and after the commit.</summary>
    private sealed class RecordingSink : IEntityChangeSink
    {
        private readonly List<EntityChange> _pending = [];

        public List<EntityChange> Committed { get; } = [];

        public int Scans { get; private set; }

        public int Discards { get; private set; }

        public void Saving(DbContext context, IReadOnlyList<EntityChangeEntry> changes)
        {
            Scans++;
            _pending.AddRange(changes.Select(c => c.Change));
        }

        public Task CommittedAsync(CancellationToken cancellationToken)
        {
            Committed.AddRange(_pending);
            _pending.Clear();
            return Task.CompletedTask;
        }

        public void Discarded()
        {
            Discards++;
            _pending.Clear();
        }
    }

    private sealed class ParcelHandler
    {
        public bool FailFirst { get; set; }

        public ConcurrentQueue<(EntityChange<Parcel> Change, Guid? User, Guid? Tenant)> Received { get; } = new();
    }

    private sealed class ScopedParcelHandler(ParcelHandler state, ICurrentUser currentUser) : IEntityChangeHandler<Parcel>
    {
        public Task HandleAsync(EntityChange<Parcel> domainEvent, CancellationToken cancellationToken)
        {
            if (state.FailFirst)
            {
                state.FailFirst = false;
                throw new InvalidOperationException("The carrier did not answer.");
            }

            state.Received.Enqueue((domainEvent, currentUser.UserId, currentUser.TenantId));
            return Task.CompletedTask;
        }
    }
}
