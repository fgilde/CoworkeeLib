using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Realtime.Watch;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;
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
