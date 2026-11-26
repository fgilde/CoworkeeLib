using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Realtime.Tests;

// IHasRealtimeSignals: what reaches a client says that something changed, and nothing else
public sealed class SignalTests(RealtimeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<int> SaveAsync(Func<RealtimeTestDbContext, Task> change) =>
        app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async db =>
        {
            await change(db);
            return await db.SaveChangesAsync(Ct);
        });

    private static void ShouldSayNothing(RealtimeEnvelope envelope)
    {
        envelope.Type.ShouldBe(RealtimeEventTypes.Signal);
        envelope.Payload.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Object);
        envelope.Payload.EnumerateObject().ShouldBeEmpty();
        envelope.Actor.ShouldBeNull();
        envelope.CorrelationId.ShouldBeNull();
        envelope.Payload.GetRawText().ShouldNotContain("Rule");
        envelope.Payload.GetRawText().ShouldNotContain("Secret");
    }

    [Fact]
    public async Task A_signal_carries_no_entity_name_id_property_names_or_actor_and_stays_in_the_organisation()
    {
        var (otherAdmin, otherTenant) = await app.CreateTenantAdminAsync();
        await using var mine = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await using var other = await app.ConnectAsync(otherAdmin, otherTenant);
        await mine.SubscribeAsync("tenant:rules");
        await other.SubscribeAsync("tenant:rules");
        var rule = new Rule { Secret = "a", TenantId = _setup.TenantId };

        await SaveAsync(db => { db.Add(rule); return Task.CompletedTask; });
        ShouldSayNothing(await mine.NextAsync(e => e.Topic == "tenant:rules"));
        mine.Events.Clear();
        // an update names no changed property, a delete no row; two rows of one save give one signal
        await SaveAsync(async db => (await db.Set<Rule>().SingleAsync(r => r.Id == rule.Id, Ct)).Secret = "b");
        ShouldSayNothing(await mine.NextAsync(e => e.Topic == "tenant:rules"));
        mine.Events.Clear();
        await SaveAsync(async db =>
        {
            db.Add(new Rule { Secret = "c", TenantId = _setup.TenantId });
            db.Remove(await db.Set<Rule>().SingleAsync(r => r.Id == rule.Id, Ct));
        });
        ShouldSayNothing(await mine.NextAsync(e => e.Topic == "tenant:rules"));
        await Task.Delay(500, Ct);
        mine.Events.Count(e => e.Topic == "tenant:rules").ShouldBe(1);
        other.Events.ShouldNotContain(e => e.Topic == "tenant:rules");
    }

    [Fact]
    public async Task A_global_signal_reaches_every_organisation_without_saying_who_changed_it()
    {
        var (otherAdmin, otherTenant) = await app.CreateTenantAdminAsync();
        await using var other = await app.ConnectAsync(otherAdmin, otherTenant);
        await other.SubscribeAsync("global:rules");

        await SaveAsync(db => { db.Add(new Rule { Secret = "g", Everywhere = true, TenantId = _setup.TenantId }); return Task.CompletedTask; });

        ShouldSayNothing(await other.NextAsync(e => e.Topic == "global:rules"));
    }
}
