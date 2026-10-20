using System.Diagnostics;
using Coworkee.Domain;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Versioning;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

public sealed class AnonymousRecordsTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestCurrentUser _user = new();
    private readonly FakeTimeProvider _clock = new(Day.AddHours(8).AddMinutes(31).AddSeconds(7));

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_rows_record_no_actor_no_audit_trail_and_only_the_day()
    {
        using var request = new Activity("request").Start();
        var ballot = new Ballot { Answer = "Yes" };
        ballot.Cast();
        await InDbAsync(db => db.Add(ballot));
        _clock.Advance(TimeSpan.FromHours(3));
        await InDbAsync(async db => (await db.Set<Ballot>().SingleAsync(b => b.Id == ballot.Id, Ct)).Answer = "No");
        await InDbAsync(async db => db.Remove(await db.Set<Ballot>().SingleAsync(b => b.Id == ballot.Id, Ct)));

        var stored = await QueryAsync(db => db.Set<Ballot>().IgnoreQueryFilters().SingleAsync(b => b.Id == ballot.Id, Ct));
        (stored.CreatedBy, stored.ModifiedBy, stored.DeletedBy).ShouldBe((null, null, null));
        (stored.CreatedAt, stored.ModifiedAt, stored.DeletedAt).ShouldBe((Day, Day, Day));
        stored.Id.Version.ShouldBe(4);

        (await QueryAsync(db => db.Set<AuditEntry>().Where(e => e.EntityType == nameof(Ballot)).ToListAsync(Ct))).ShouldBeEmpty();
        var snapshots = await QueryAsync(db => db.Set<EntitySnapshot>().Where(s => s.EntityType == nameof(Ballot)).ToListAsync(Ct));
        snapshots.Count.ShouldBe(3);
        snapshots.ShouldAllBe(s => s.CreatedBy == null && s.CreatedAt == Day);
        var message = (await QueryAsync(db => db.Set<OutboxMessage>().ToListAsync(Ct))).ShouldHaveSingleItem();
        (message.ActorId, message.CorrelationId).ShouldBe((null, null));
        message.TenantId.ShouldBe(_user.TenantId);
    }

    [Fact]
    public void Anonymous_entities_get_random_ids_and_may_keep_a_finer_precision()
    {
        var note = new HourlyNote();

        note.Id.Version.ShouldBe(4);
        ((IAnonymous)note).Truncate(_clock.GetUtcNow()).ShouldBe(Day.AddHours(8));
        new Ballot { Answer = "x" }.Truncate(new DateTimeOffset(2026, 10, 1, 1, 0, 0, TimeSpan.FromHours(2))).ShouldBe(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero));
    }

    private async Task InDbAsync(Func<TestDbContext, Task> action)
    {
        await using var provider = database.CreateServices(_user, _clock);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await action(db);
        await db.SaveChangesAsync(Ct);
    }

    private Task InDbAsync(Action<TestDbContext> action) => InDbAsync(db =>
    {
        action(db);
        return Task.CompletedTask;
    });

    private async Task<T> QueryAsync<T>(Func<TestDbContext, Task<T>> query)
    {
        await using var provider = database.CreateServices(_user, _clock);
        await using var scope = provider.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }
}
