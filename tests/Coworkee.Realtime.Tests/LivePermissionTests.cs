using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Coworkee.Realtime.Tests;

/// <summary>Permission changes reach the clients of the organisation, and an active role narrows what a user may do on the server.</summary>
public sealed class LivePermissionTests(RealtimeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.App.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Assigning_a_role_tells_the_organisation_and_the_new_permissions_are_served_at_once()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var readers = await CreateRoleAsync("Ticket readers", Ticket.ViewPermission);
        await using var listener = await app.ConnectAsync(bob.Id, _setup.TenantId);
        await listener.SubscribeAsync(RealtimeTopics.TenantPermissions);
        (await PermissionsOfAsync(bob.Id)).ShouldNotContain(Ticket.ViewPermission);

        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{bob.Id}/roles", new IdListRequest([readers]), Ct)).EnsureSuccessStatusCode();

        await listener.NextAsync(e => e.Topic == RealtimeTopics.TenantPermissions);
        (await PermissionsOfAsync(bob.Id)).ShouldContain(Ticket.ViewPermission);
    }

    [Fact]
    public async Task Editing_the_grants_of_a_role_tells_the_organisation()
    {
        var readers = await CreateRoleAsync("Ticket readers");
        await using var listener = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await listener.SubscribeAsync(RealtimeTopics.TenantPermissions);

        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{readers}", new NameListRequest([Ticket.ViewPermission]), Ct)).EnsureSuccessStatusCode();

        await listener.NextAsync(e => e.Topic == RealtimeTopics.TenantPermissions);
    }

    [Fact]
    public async Task An_active_role_narrows_the_permissions_to_that_role_and_never_widens_them()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var readers = await CreateRoleAsync("Ticket readers", Ticket.ViewPermission);
        var viewers = await CreateRoleAsync("User viewers", IdentityPermissions.Users.View);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{bob.Id}/roles", new IdListRequest([readers, viewers]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/User/{bob.Id}", new NameListRequest([IdentityPermissions.Roles.View]), Ct)).EnsureSuccessStatusCode();
        var asBob = app.App.GetTestClient().AsUser(bob.Id, _setup.TenantId);

        var mine = (await asBob.GetFromJsonAsync<MyRolesDto>($"/api/v1/identity/roles/me?permission={Ticket.ViewPermission}", Ct))!;
        (mine.Enabled, mine.ActiveRoleId).ShouldBe((true, null));
        mine.Roles.Single(r => r.Id == readers).Grants.ShouldBeTrue();
        mine.Roles.Single(r => r.Id == viewers).Grants.ShouldBeFalse();

        (await asBob.PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(viewers), Ct)).EnsureSuccessStatusCode();

        var narrowed = await PermissionsOfAsync(bob.Id);
        narrowed.ShouldContain(IdentityPermissions.Users.View);
        narrowed.ShouldNotContain(Ticket.ViewPermission);
        narrowed.ShouldNotContain(IdentityPermissions.Roles.View);
        (await asBob.GetFromJsonAsync<MyRolesDto>("/api/v1/identity/roles/me", Ct))!.ActiveRoleId.ShouldBe(viewers);
        (await asBob.GetAsync("/api/v1/identity/roles", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await asBob.PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(null), Ct)).EnsureSuccessStatusCode();

        (await PermissionsOfAsync(bob.Id)).ShouldContain(Ticket.ViewPermission);
        (await asBob.GetAsync("/api/v1/identity/roles", Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Only_an_own_role_can_become_active_and_switching_tells_the_organisation()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var readers = await CreateRoleAsync("Ticket readers", Ticket.ViewPermission);
        var other = await CreateRoleAsync("Others");
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{bob.Id}/roles", new IdListRequest([readers]), Ct)).EnsureSuccessStatusCode();
        var asBob = app.App.GetTestClient().AsUser(bob.Id, _setup.TenantId);
        await using var listener = await app.ConnectAsync(bob.Id, _setup.TenantId);
        await listener.SubscribeAsync(RealtimeTopics.TenantPermissions);

        (await asBob.PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(other), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await asBob.PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(readers), Ct)).EnsureSuccessStatusCode();

        await listener.NextAsync(e => e.Topic == RealtimeTopics.TenantPermissions);
    }

    [Fact]
    public async Task Deactivating_a_user_ends_his_sessions()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        await using var listener = await app.ConnectAsync(bob.Id, _setup.TenantId);
        await listener.SubscribeAsync(RealtimeTopics.User(bob.Id));

        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{bob.Id}", new UpdateUserRequest(null, null, false), Ct)).EnsureSuccessStatusCode();

        await listener.NextAsync(e => e.Type == RealtimeEventTypes.SessionRevoked);
    }

    private async Task<UserDto> CreateUserAsync(string email) =>
        (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;

    private async Task<Guid> CreateRoleAsync(string name, params string[] permissions)
    {
        var role = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        if (permissions.Length > 0)
        {
            (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        }

        return role;
    }

    private async Task<string[]> PermissionsOfAsync(Guid userId) =>
        (await app.App.GetTestClient().AsUser(userId, _setup.TenantId).GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct))!;
}
