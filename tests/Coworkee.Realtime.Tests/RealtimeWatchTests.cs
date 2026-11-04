using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Realtime.Watch;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Realtime.Tests;

/// <summary>
/// Rows another system writes (no SaveChanges of the app, no [Realtime]) reach every page that shows them: the watch reads what changed since it last
/// looked and sends the events of a change made in the app, to the organisations that follow the set; nothing is read while nobody follows.
/// </summary>
public sealed class RealtimeWatchTests(RealtimeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RealtimeWatches Watches => app.App.Services.GetRequiredService<RealtimeWatches>();

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_new_row_written_outside_the_app_reaches_the_followers_of_its_type()
    {
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync("type:Parcel");
        await WatchedAsync();

        var id = await WriteAsync(new Parcel { Code = "P-1", CreatedUtc = DateTimeOffset.UtcNow });

        var envelope = await admin.NextAsync(e => e.Topic == "type:Parcel");
        envelope.Type.ShouldBe(RealtimeEventTypes.EntityChanged);
        envelope.Payload.GetProperty("entityType").GetString().ShouldBe(nameof(Parcel));
        envelope.Payload.GetProperty("entityId").GetString().ShouldBe(id.ToString());
        envelope.Payload.GetProperty("action").GetString().ShouldBe(EntityChangedPayload.Created);
        // a look reads the key and the dates, not the rows (a hub shipment has a hundred columns)
        var looks = app.Logs.Lines.Where(l => l.Contains("UpdatedUtc", StringComparison.Ordinal) && l.Contains("Parcel", StringComparison.Ordinal) && l.Contains(" > ", StringComparison.Ordinal)).ToList();
        looks.ShouldNotBeEmpty();
        looks.ShouldAllBe(l => !l.Contains("Code", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_first_row_of_an_empty_set_reaches_the_followers()
    {
        await app.AsActorAsync<int>(null, null, db => db.Set<Parcel>().ExecuteDeleteAsync(Ct));
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync("type:Parcel");
        await WatchedAsync();

        var id = await WriteAsync(new Parcel { Code = "First", CreatedUtc = DateTimeOffset.UtcNow });

        (await admin.NextAsync(e => e.Topic == "type:Parcel")).Payload.GetProperty("entityId").GetString().ShouldBe(id.ToString());
    }

    [Fact]
    public async Task A_changed_row_reaches_the_followers_of_that_row()
    {
        var id = await WriteAsync(new Parcel { Code = "P-2", CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5) });
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync($"entity:Parcel:{id}");
        await WatchedAsync();

        await app.AsActorAsync<int>(null, null, async db =>
        {
            var parcel = await db.Set<Parcel>().FindAsync([id], Ct);
            parcel!.UpdatedUtc = DateTimeOffset.UtcNow;
            return await db.SaveChangesAsync(Ct);
        });

        var envelope = await admin.NextAsync(e => e.Topic == $"entity:Parcel:{id}");
        envelope.Payload.GetProperty("action").GetString().ShouldBe(EntityChangedPayload.Updated);
    }

    [Fact]
    public async Task Rows_sent_already_neither_come_again_nor_count_against_the_most_events_of_a_look()
    {
        // written before anyone follows, within the look back: not sent when someone starts following
        await WriteAsync(new Parcel { Code = "Before", CreatedUtc = DateTimeOffset.UtcNow });
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync("type:Parcel");
        await WatchedAsync();

        // three rows (MaxEvents 3) one by one, then a fourth: the look back reads all four, three of them sent already
        var ids = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            ids.Add((await WriteAsync(new Parcel { Code = $"P-{i}", CreatedUtc = DateTimeOffset.UtcNow })).ToString());
            await WatchedAsync();
        }

        await WatchedAsync();
        var sent = admin.Events.Where(e => e.Topic == "type:Parcel").Select(e => e.Payload.GetProperty("entityId").GetString()).ToList();
        sent.ShouldBe(ids);
    }

    [Fact]
    public async Task A_watch_switched_off_reads_nothing_until_it_is_switched_on_again()
    {
        var admin = app.App.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);
        await using var listener = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await listener.SubscribeAsync("type:Parcel");
        await WatchedAsync();
        try
        {
            (await admin.PutAsJsonAsync("/api/v1/realtime/watches/Parcel", new SetRealtimeWatchRequest(false), Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
            (await admin.GetFromJsonAsync<RealtimeWatchDto[]>("/api/v1/realtime/watches", Ct))!.ShouldContain(w => w.EntityType == nameof(Parcel) && !w.Enabled);
            var polls = Watches.Polls(typeof(Parcel));

            var off = await WriteAsync(new Parcel { Code = "Off", CreatedUtc = DateTimeOffset.UtcNow });
            await Task.Delay(1000, Ct);

            Watches.Polls(typeof(Parcel)).ShouldBe(polls);
            listener.Events.ShouldNotContain(e => e.Topic == "type:Parcel");

            (await admin.PutAsJsonAsync("/api/v1/realtime/watches/Parcel", new SetRealtimeWatchRequest(true), Ct)).EnsureSuccessStatusCode();
            await WatchedAsync();
            var on = await WriteAsync(new Parcel { Code = "On", CreatedUtc = DateTimeOffset.UtcNow });

            // switched on again it starts from the newest rows, like a first follower: what was written while it was off is not sent
            (await listener.NextAsync(e => e.Topic == "type:Parcel")).Payload.GetProperty("entityId").GetString().ShouldBe(on.ToString());
            listener.Events.ShouldNotContain(e => e.Topic == "type:Parcel" && e.Payload.GetProperty("entityId").GetString() == off.ToString());
        }
        finally
        {
            await admin.PutAsJsonAsync("/api/v1/realtime/watches/Parcel", new SetRealtimeWatchRequest(true), Ct);
        }
    }

    [Fact]
    public async Task Switching_a_watch_needs_the_permission_to_manage_watches()
    {
        var admin = app.App.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);
        var bob = (await (await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("carol@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var readers = (await (await admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Parcel readers", null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{readers}", new NameListRequest([Ticket.ViewPermission]), Ct)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/v1/identity/users/{bob.Id}/roles", new IdListRequest([readers]), Ct)).EnsureSuccessStatusCode();
        var asBob = app.App.GetTestClient().AsUser(bob.Id, _setup.TenantId);

        // who may follow the set sees whether it is watched, but may not switch it
        (await asBob.GetFromJsonAsync<RealtimeWatchDto[]>("/api/v1/realtime/watches", Ct))!.ShouldContain(w => w.EntityType == nameof(Parcel) && !w.CanManage);
        (await asBob.PutAsJsonAsync("/api/v1/realtime/watches/Parcel", new SetRealtimeWatchRequest(false), Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
        (await admin.GetFromJsonAsync<RealtimeWatchDto[]>("/api/v1/realtime/watches", Ct))!.ShouldContain(w => w.EntityType == nameof(Parcel) && w.Enabled && w.CanManage);
    }

    [Fact]
    public async Task Following_a_watched_set_needs_its_view_permission()
    {
        var admin = app.App.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);
        var bob = (await (await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var nothing = (await (await admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Nothing", null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await admin.PutAsJsonAsync($"/api/v1/identity/users/{bob.Id}/roles", new IdListRequest([nothing]), Ct)).EnsureSuccessStatusCode();
        await using var listener = await app.ConnectAsync(bob.Id, _setup.TenantId);

        await Should.ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>(() => listener.SubscribeAsync("type:Parcel"));
    }

    [Fact]
    public async Task Nothing_is_read_while_nobody_follows_the_set()
    {
        await Task.Delay(500, Ct);
        var polls = Watches.Polls(typeof(Parcel));

        await Task.Delay(1000, Ct);

        Watches.Polls(typeof(Parcel)).ShouldBe(polls);
    }

    // the watch has looked once since someone follows the set: what is written from now on is new to it
    private async Task WatchedAsync()
    {
        var polls = Watches.Polls(typeof(Parcel));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Watches.Polls(typeof(Parcel)) < polls + 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, Ct);
        }
    }

    private Task<Guid> WriteAsync(Parcel parcel) => app.AsActorAsync(null, null, async db =>
    {
        db.Add(parcel);
        await db.SaveChangesAsync(Ct);
        return parcel.Id;
    });
}
