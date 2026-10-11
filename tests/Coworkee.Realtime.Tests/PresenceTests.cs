using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Realtime.Tests;

public sealed class PresenceTests(RealtimeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        app.App.Services.GetRequiredService<RealtimePresence>().Grace = TimeSpan.FromMilliseconds(300);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_is_online_while_any_connection_lives_and_offline_after_the_last_one()
    {
        var bob = (await (await app.App.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId)
            .PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync(PresenceEvents.Topic);

        var first = await app.ConnectAsync(bob.Id, _setup.TenantId);
        var second = await app.ConnectAsync(bob.Id, _setup.TenantId);
        await admin.NextAsync(e => Is(e, bob.Id, true));
        (await OnlineAsync()).ShouldContain(bob.Id);

        await first.DisposeAsync();
        await Task.Delay(800, Ct);
        admin.Events.ShouldNotContain(e => Is(e, bob.Id, false));
        admin.Events.Count(e => Is(e, bob.Id, true)).ShouldBe(1);

        await second.DisposeAsync();
        await admin.NextAsync(e => Is(e, bob.Id, false));
        (await OnlineAsync()).ShouldNotContain(bob.Id);
    }

    private static bool Is(RealtimeEnvelope envelope, Guid userId, bool online) =>
        envelope.Type == PresenceEvents.Changed && envelope.Payload.GetProperty("userId").GetGuid() == userId && envelope.Payload.GetProperty("online").GetBoolean() == online;

    private async Task<Guid[]> OnlineAsync() =>
        (await app.App.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId).GetFromJsonAsync<Guid[]>(PresenceEvents.Path, Ct))!;
}
