using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;

namespace Coworkee.Identity.Tests;

/// <summary>Single role and grant changes of the admin user page, and the user's permissions with their sources.</summary>
public sealed class UserAssignmentTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Adding_and_removing_one_role_keeps_the_others()
    {
        var user = await UserAsync("eve");
        var (a, b, c) = (await RoleAsync("A"), await RoleAsync("B"), await RoleAsync("C"));
        await PutAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([a]));

        // two administrators at once: each adds his own role, neither undoes the other
        (await Admin.PostAsync($"/api/v1/identity/users/{user}/roles/{b}", null, Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsync($"/api/v1/identity/users/{user}/roles/{c}", null, Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsync($"/api/v1/identity/users/{user}/roles/{c}", null, Ct)).EnsureSuccessStatusCode();
        (await RolesAsync(user)).ShouldBe([a, b, c], ignoreOrder: true);

        (await Admin.DeleteAsync($"/api/v1/identity/users/{user}/roles/{b}", Ct)).EnsureSuccessStatusCode();
        (await Admin.DeleteAsync($"/api/v1/identity/users/{user}/roles/{b}", Ct)).EnsureSuccessStatusCode();
        (await RolesAsync(user)).ShouldBe([a, c], ignoreOrder: true);

        (await Admin.PostAsync($"/api/v1/identity/users/{user}/roles/{Guid.CreateVersion7()}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_last_administrator_keeps_his_role()
    {
        var adminRole = (await Admin.GetFromJsonAsync<RoleDto[]>("/api/v1/identity/roles", Ct))!.Single(r => r.IsSystem && r.Name == "admin").Id;

        (await Admin.DeleteAsync($"/api/v1/identity/users/{_setup.AdminUserId}/roles/{adminRole}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await RolesAsync(_setup.AdminUserId)).ShouldContain(adminRole);
    }

    [Fact]
    public async Task Single_role_changes_need_users_manage_and_stay_in_the_tenant()
    {
        var user = await UserAsync("eve");
        var role = await RoleAsync("A");
        var viewer = await ClientWithAsync(IdentityPermissions.Users.View);
        var stranger = await StrangerAsync();

        (await viewer.PostAsync($"/api/v1/identity/users/{user}/roles/{role}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"/api/v1/identity/users/{user}/roles/{role}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.PostAsync($"/api/v1/identity/users/{stranger}/roles/{role}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.DeleteAsync($"/api/v1/identity/users/{stranger}/roles/{role}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RolesAsync(user)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Granting_and_revoking_one_permission_keeps_the_others()
    {
        var user = await UserAsync("eve");
        await PutAsync($"/api/v1/identity/permissions/grants/User/{user}", new NameListRequest([IdentityPermissions.Users.View]));

        (await Admin.PostAsync($"/api/v1/identity/permissions/grants/User/{user}/{IdentityPermissions.Roles.View}", null, Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsync($"/api/v1/identity/permissions/grants/User/{user}/{IdentityPermissions.Groups.View}", null, Ct)).EnsureSuccessStatusCode();
        (await GrantsAsync(user)).ShouldBe([IdentityPermissions.Groups.View, IdentityPermissions.Roles.View, IdentityPermissions.Users.View], ignoreOrder: true);

        (await Admin.DeleteAsync($"/api/v1/identity/permissions/grants/User/{user}/{IdentityPermissions.Roles.View}", Ct)).EnsureSuccessStatusCode();
        (await Admin.DeleteAsync($"/api/v1/identity/permissions/grants/User/{user}/{IdentityPermissions.Roles.View}", Ct)).EnsureSuccessStatusCode();
        (await GrantsAsync(user)).ShouldBe([IdentityPermissions.Groups.View, IdentityPermissions.Users.View], ignoreOrder: true);

        (await Admin.PostAsync($"/api/v1/identity/permissions/grants/User/{user}/Nope.Unknown", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Single_grants_need_permissions_manage_and_stay_in_the_tenant()
    {
        var user = await UserAsync("eve");
        var manager = await ClientWithAsync(IdentityPermissions.Users.Manage);
        var stranger = await StrangerAsync();

        (await manager.PostAsync($"/api/v1/identity/permissions/grants/User/{user}/{IdentityPermissions.Users.View}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.DeleteAsync($"/api/v1/identity/permissions/grants/User/{user}/{IdentityPermissions.Users.View}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.PostAsync($"/api/v1/identity/permissions/grants/User/{stranger}/{IdentityPermissions.Users.View}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GrantsAsync(user)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_permissions_of_a_user_name_every_source()
    {
        var user = await UserAsync("eve");
        var viewers = await RoleAsync("Viewers");
        var managers = await RoleAsync("Group managers");
        await PutAsync($"/api/v1/identity/permissions/grants/Role/{viewers}", new NameListRequest([IdentityPermissions.Users.View]));
        await PutAsync($"/api/v1/identity/permissions/grants/Role/{managers}", new NameListRequest([IdentityPermissions.Groups.Manage]));
        await PutAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([viewers]));
        var team = await GroupAsync("Team");
        await PutAsync($"/api/v1/identity/groups/{team}/roles", new IdListRequest([managers]));
        await PutAsync($"/api/v1/identity/groups/{team}/members", new IdListRequest([user]));
        await PutAsync($"/api/v1/identity/permissions/grants/Group/{team}", new NameListRequest([IdentityPermissions.Roles.View]));
        await PutAsync($"/api/v1/identity/permissions/grants/User/{user}", new NameListRequest([IdentityPermissions.Users.View, IdentityPermissions.Sessions.View]));

        var result = (await Admin.GetFromJsonAsync<UserPermissionsDto>($"/api/v1/identity/users/{user}/permissions/sources", Ct))!;
        var sources = result.Permissions.ToDictionary(p => p.Name, p => p.Sources);

        result.Granted.ShouldBe([IdentityPermissions.Sessions.View, IdentityPermissions.Users.View]);
        // also implied by what the group's role grants
        sources[IdentityPermissions.Users.View].ShouldBe([
            new PermissionSourceDto(PermissionSourceKind.Role, viewers, "Viewers"),
            new PermissionSourceDto(PermissionSourceKind.Role, managers, "Group managers", team, "Team"),
            new PermissionSourceDto(PermissionSourceKind.Direct),
        ], ignoreOrder: true);
        // implied by Groups.Manage: the source of the granted permission
        sources[IdentityPermissions.Groups.View].ShouldBe([new PermissionSourceDto(PermissionSourceKind.Role, managers, "Group managers", team, "Team")]);
        sources[IdentityPermissions.Roles.View].ShouldBe([new PermissionSourceDto(PermissionSourceKind.Group, team, "Team")]);
        sources[IdentityPermissions.Sessions.View].ShouldBe([new PermissionSourceDto(PermissionSourceKind.Direct)]);
        sources.Keys.Order(StringComparer.Ordinal).ShouldBe(
            (await Admin.GetFromJsonAsync<string[]>($"/api/v1/identity/users/{user}/permissions", Ct))!.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_computed_permission_names_no_role()
    {
        var user = await UserAsync("lead");
        ComputedPermissionTests.Leaders[user] = true;
        try
        {
            var result = (await Admin.GetFromJsonAsync<UserPermissionsDto>($"/api/v1/identity/users/{user}/permissions/sources", Ct))!;

            result.Permissions.Single(p => p.Name == ComputedPermissionTests.Permission).Sources.ShouldBe([new PermissionSourceDto(PermissionSourceKind.Computed)]);
        }
        finally
        {
            ComputedPermissionTests.Leaders.TryRemove(user, out _);
        }
    }

    [Fact]
    public async Task The_permissions_of_a_user_need_users_view_and_stay_in_the_tenant()
    {
        var user = await UserAsync("eve");
        var stranger = await StrangerAsync();
        var roles = await ClientWithAsync(IdentityPermissions.Roles.View);

        (await roles.GetAsync($"/api/v1/identity/users/{user}/permissions/sources", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.GetAsync($"/api/v1/identity/users/{stranger}/permissions/sources", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<HttpClient> ClientWithAsync(string permission)
    {
        var user = await UserAsync("limited");
        var role = await RoleAsync("Limited");
        await PutAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([permission]));
        await PutAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([role]));
        return app.As(user, _setup.TenantId);
    }

    private Task<Guid> StrangerAsync() => app.InDbAsync(null, db =>
    {
        var tenant = new Tenant { Name = "Other", Identifier = "other" };
        var user = new User { TenantId = tenant.Id, UserName = "x@other.test", Email = "x@other.test", NormalizedEmail = "X@OTHER.TEST", NormalizedUserName = "X@OTHER.TEST" };
        db.AddRange(tenant, user);
        return Task.FromResult(user.Id);
    });

    private async Task<Guid> UserAsync(string name) =>
        (await PostAsync<UserDto>("/api/v1/identity/users", new CreateUserRequest($"{name}@acme.test", "Passw0rd!x", null, null))).Id;

    private Task<Guid> RoleAsync(string name) => PostAsync<Guid>("/api/v1/identity/roles", new RoleRequest(name, null));

    private Task<Guid> GroupAsync(string name) => PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest(name, null));

    private async Task<IReadOnlyList<Guid>> RolesAsync(Guid user) =>
        [.. (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{user}", Ct))!.Roles.Select(r => r.Id)];

    private async Task<string[]> GrantsAsync(Guid user) => (await Admin.GetFromJsonAsync<string[]>($"/api/v1/identity/permissions/grants/User/{user}", Ct))!;

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Admin.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task PutAsync(string url, object body) => (await Admin.PutAsJsonAsync(url, body, Ct)).EnsureSuccessStatusCode();
}
