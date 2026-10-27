using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Coworkee.Realtime.Tests;

/// <summary>The membership and role link a new user gets by default are published like any other change.</summary>
public sealed class NewUserDefaultsRealtimeTests(RealtimeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.App.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Joining_the_default_group_and_getting_the_default_role_reach_the_group_and_user_topics()
    {
        var group = await (await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("Default", null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync("/api/v1/identity/groups/default", new DefaultGroupDto(group), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Customer", null, IsDefault: true), Ct)).EnsureSuccessStatusCode();
        await using var listener = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await listener.SubscribeAsync(RealtimeTopics.Entity("UserGroup", group));

        var created = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("new@acme.test", "Passw0rd!x", null, null), Ct);
        created.EnsureSuccessStatusCode();


        (await listener.NextAsync(e => e.Topic == RealtimeTopics.Entity("UserGroup", group))).ShouldNotBeNull();
    }
}
