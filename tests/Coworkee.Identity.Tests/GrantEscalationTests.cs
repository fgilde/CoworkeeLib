using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;

namespace Coworkee.Identity.Tests;

/// <summary>Every way to hand out permissions: a caller grants or revokes only what he holds himself, and only administrators hand out the administrator role.</summary>
public sealed class GrantEscalationTests(IdentityApp app) : IAsyncLifetime
{
    private const string Restricted = "identity.grant_restricted";
    private const string Lacking = IdentityPermissions.Users.Impersonate;
    private const string Held = IdentityPermissions.Groups.View;

    private SetupResultDto _setup = null!;
    private Guid _adminRole;
    private Guid _manager;
    private Guid _peer;
    private Guid _lackingRole;
    private Guid _heldRole;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
        _adminRole = (await Admin.GetFromJsonAsync<RoleDto[]>("/api/v1/identity/roles", Ct))!.Single(r => r.Name == SystemRoles.Admin).Id;
        _manager = await UserWithAsync("manager@acme.test",
            IdentityPermissions.Users.Manage, IdentityPermissions.Roles.Manage, IdentityPermissions.Permissions.Manage, IdentityPermissions.Groups.Manage,
            IdentityPermissions.ResourcePermissions.Manage);
        _peer = await UserWithAsync("peer@acme.test", IdentityPermissions.Users.View);
        _lackingRole = await RoleWithAsync("Impersonators", Lacking);
        _heldRole = await RoleWithAsync("Group viewers", Held);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Manager => app.As(_manager, _setup.TenantId);

    [Theory]
    [InlineData(PermissionProviderType.Role)]
    [InlineData(PermissionProviderType.User)]
    [InlineData(PermissionProviderType.Group)]
    public async Task Grants_and_revocations_stay_within_the_callers_permissions(PermissionProviderType type)
    {
        var key = type switch
        {
            PermissionProviderType.Role => await RoleWithAsync("Plain"),
            PermissionProviderType.User => _peer,
            _ => await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Plain", null)),
        };
        var url = $"/api/v1/identity/permissions/grants/{type}/{key}";

        await RefusedAsync(await Manager.PutAsJsonAsync(url, new NameListRequest([Lacking]), Ct));
        (await Manager.PutAsJsonAsync(url, new NameListRequest([Held]), Ct)).EnsureSuccessStatusCode();

        (await Admin.PutAsJsonAsync(url, new NameListRequest([Held, Lacking]), Ct)).EnsureSuccessStatusCode();
        var revoke = await Manager.PutAsJsonAsync(url, new NameListRequest([Held]), Ct);
        if (type == PermissionProviderType.User)
        {
            // the user holds more than the manager now: his account is out of reach
            revoke.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await CodeAsync(revoke)).ShouldBe("identity.user_more_permissions");
            return;
        }

        await RefusedAsync(revoke);
        (await Manager.PutAsJsonAsync(url, new NameListRequest([Lacking]), Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Implied_permissions_count_on_both_sides()
    {
        var url = $"/api/v1/identity/permissions/grants/Role/{await RoleWithAsync("Plain")}";

        // the manager holds Users.View only as implied by Users.Manage
        (await Manager.PutAsJsonAsync(url, new NameListRequest([IdentityPermissions.Users.View]), Ct)).EnsureSuccessStatusCode();
        (await Manager.PutAsJsonAsync(url, new NameListRequest([IdentityPermissions.Groups.Manage]), Ct)).EnsureSuccessStatusCode();
        await RefusedAsync(await Manager.PutAsJsonAsync(url, new NameListRequest([IdentityPermissions.Clients.Manage]), Ct));
    }

    [Fact]
    public async Task User_roles_stay_within_the_callers_permissions_and_the_admin_role_needs_an_admin()
    {
        var url = $"/api/v1/identity/users/{_peer}/roles";
        var current = (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{_peer}", Ct))!.Roles.Select(r => r.Id).ToList();

        await RefusedAsync(await Manager.PutAsJsonAsync(url, new IdListRequest([.. current, _lackingRole]), Ct));
        var admin = await Manager.PutAsJsonAsync(url, new IdListRequest([.. current, _adminRole]), Ct);
        admin.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(admin)).ShouldBe("identity.admin_role_restricted");
        (await Manager.PutAsJsonAsync(url, new IdListRequest([.. current, _heldRole]), Ct)).EnsureSuccessStatusCode();


        // once the user holds the role, he holds more than the manager: the account is out of reach
        (await Admin.PutAsJsonAsync(url, new IdListRequest([.. current, _heldRole, _lackingRole]), Ct)).EnsureSuccessStatusCode();
        (await Manager.PutAsJsonAsync(url, new IdListRequest([.. current, _heldRole]), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task New_users_get_only_roles_within_the_callers_permissions()
    {
        await RefusedAsync(await Manager.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("a@acme.test", "Passw0rd!x", null, null, RoleIds: [_lackingRole]), Ct));
        (await Manager.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("b@acme.test", "Passw0rd!x", null, null, RoleIds: [_adminRole]), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Manager.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("c@acme.test", "Passw0rd!x", null, null, RoleIds: [_heldRole]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("d@acme.test", "Passw0rd!x", null, null, RoleIds: [_lackingRole]), Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Group_roles_memberships_and_deletions_stay_within_the_callers_permissions()
    {
        var group = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Team", null));
        var groupUrl = $"/api/v1/identity/groups/{group}";

        await RefusedAsync(await Manager.PutAsJsonAsync($"{groupUrl}/roles", new IdListRequest([_lackingRole]), Ct));
        (await Manager.PutAsJsonAsync($"{groupUrl}/roles", new IdListRequest([_heldRole]), Ct)).EnsureSuccessStatusCode();

        // joining or leaving a group with a role he lacks hands that role out or takes it
        (await Admin.PutAsJsonAsync($"{groupUrl}/roles", new IdListRequest([_heldRole, _lackingRole]), Ct)).EnsureSuccessStatusCode();
        await RefusedAsync(await Manager.PostAsJsonAsync($"{groupUrl}/members/add", new IdListRequest([_peer]), Ct));
        await RefusedAsync(await Manager.PutAsJsonAsync($"/api/v1/identity/users/{_peer}/groups", new IdListRequest([group]), Ct));
        await RefusedAsync(await Manager.PutAsJsonAsync($"{groupUrl}/roles", new IdListRequest([_heldRole]), Ct));
        await RefusedAsync(await Manager.DeleteAsync(groupUrl, Ct));
        (await Admin.PostAsJsonAsync($"{groupUrl}/members/add", new IdListRequest([_peer]), Ct)).EnsureSuccessStatusCode();
        (await Manager.PostAsJsonAsync($"{groupUrl}/members/remove", new IdListRequest([_peer]), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // a group with what he holds is his to fill
        var held = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Viewers", null));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Group/{held}", new NameListRequest([Held]), Ct)).EnsureSuccessStatusCode();
        var fresh = await UserWithAsync("fresh@acme.test", IdentityPermissions.Users.View);
        (await Manager.PostAsJsonAsync($"/api/v1/identity/groups/{held}/members/add", new IdListRequest([fresh]), Ct)).EnsureSuccessStatusCode();
        (await Manager.DeleteAsync($"/api/v1/identity/groups/{held}", Ct)).EnsureSuccessStatusCode();
        (await Admin.DeleteAsync(groupUrl, Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Group_grants_the_caller_lacks_keep_him_from_changing_its_members()
    {
        var group = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Granted", null));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Group/{group}", new NameListRequest([Lacking]), Ct)).EnsureSuccessStatusCode();

        await RefusedAsync(await Manager.PostAsJsonAsync($"/api/v1/identity/groups/{group}/members/add", new IdListRequest([_peer]), Ct));
        await RefusedAsync(await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{group}/owners", new IdListRequest([_peer]), Ct));
    }

    [Fact]
    public async Task Deleting_a_role_takes_its_permissions_from_its_members()
    {
        await RefusedAsync(await Manager.DeleteAsync($"/api/v1/identity/roles/{_lackingRole}", Ct));
        (await Manager.DeleteAsync($"/api/v1/identity/roles/{_heldRole}", Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Resource_grants_need_the_roles_permissions_globally_or_on_the_resource()
    {
        var folder = Guid.CreateVersion7();
        var url = $"/api/v1/identity/resource-permissions/Folder/{folder}";

        await RefusedAsync(await Manager.PostAsJsonAsync(url, new GrantResourcePermissionRequest(PrincipalType.User, _peer, _lackingRole), Ct));
        (await Manager.PostAsJsonAsync(url, new GrantResourcePermissionRequest(PrincipalType.User, _peer, _heldRole), Ct)).EnsureSuccessStatusCode();

        var granted = await PostAsync<Guid>(Admin, url, new GrantResourcePermissionRequest(PrincipalType.User, _peer, _lackingRole));
        await RefusedAsync(await Manager.DeleteAsync($"{url}/{granted}", Ct));

        // the same role on this one folder lets him pass it on there
        (await Admin.PostAsJsonAsync(url, new GrantResourcePermissionRequest(PrincipalType.User, _manager, _lackingRole), Ct)).EnsureSuccessStatusCode();
        (await Manager.DeleteAsync($"{url}/{granted}", Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Default_roles_stay_assignable_as_every_new_user_gets_them()
    {
        (await Admin.PutAsJsonAsync($"/api/v1/identity/roles/{_lackingRole}", new RoleRequest("Impersonators", null, IsDefault: true), Ct)).EnsureSuccessStatusCode();
        var current = (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{_peer}", Ct))!.Roles.Select(r => r.Id).ToList();

        (await Manager.PutAsJsonAsync($"/api/v1/identity/users/{_peer}/roles", new IdListRequest([.. current, _lackingRole]), Ct)).EnsureSuccessStatusCode();
    }

    private static async Task RefusedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).ShouldBe(Restricted);
    }

    private async Task<Guid> RoleWithAsync(string name, params string[] permissions)
    {
        var role = await PostAsync<Guid>(Admin, "/api/v1/identity/roles", new RoleRequest(name, null));
        if (permissions.Length > 0)
        {
            (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        }

        return role;
    }

    private async Task<Guid> UserWithAsync(string email, params string[] permissions)
    {
        var user = await PostAsync<UserDto>(Admin, "/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null));
        var role = await RoleWithAsync("Role " + email, permissions);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return user.Id;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();
}
