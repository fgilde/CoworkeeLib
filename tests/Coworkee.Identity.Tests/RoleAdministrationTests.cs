using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Tests;

/// <summary>Renaming roles and copying or moving the users of a role into other roles.</summary>
public sealed class RoleAdministrationTests(IdentityApp app) : IAsyncLifetime
{
    /// <summary>Set in <c>Coworkee:Identity:FixedRoleNames</c> of the test app: code looks the role up by this name.</summary>
    public const string FixedName = "Approvers";

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
    public async Task A_renamed_role_keeps_its_permissions_and_users_and_its_description_changes()
    {
        var role = await RoleAsync("Clerks", IdentityPermissions.Users.View);
        var user = await UserAsync("clerk", role);

        (await Admin.PutAsJsonAsync($"/api/v1/identity/roles/{role}", new RoleRequest(" Office ", "Front desk"), Ct)).EnsureSuccessStatusCode();

        var renamed = (await RolesAsync()).Single(r => r.Id == role);
        (renamed.Name, renamed.Description).ShouldBe(("Office", "Front desk"));
        (await Admin.GetFromJsonAsync<string[]>($"/api/v1/identity/permissions/grants/Role/{role}", Ct)).ShouldBe([IdentityPermissions.Users.View]);
        (await RoleIdsAsync(user)).ShouldBe([role]);
        (await Admin.GetFromJsonAsync<string[]>($"/api/v1/identity/users/{user}/permissions", Ct))!.ShouldContain(IdentityPermissions.Users.View);
    }

    [Fact]
    public async Task Fixed_and_system_roles_keep_their_names_and_renames_need_Roles_Manage()
    {
        var fixedRole = await RoleAsync(FixedName);
        var admin = (await RolesAsync()).Single(r => r.Name == SystemRoles.Admin).Id;
        var other = await RoleAsync("Other");

        var renamed = await Admin.PutAsJsonAsync($"/api/v1/identity/roles/{fixedRole}", new RoleRequest("Deciders", null), Ct);
        (renamed.StatusCode, await CodeAsync(renamed)).ShouldBe((HttpStatusCode.Conflict, "identity.role_name_fixed"));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/roles/{fixedRole}", new RoleRequest(FixedName, "Decide on approvals"), Ct)).EnsureSuccessStatusCode();
        (await RolesAsync()).Single(r => r.Id == fixedRole).Description.ShouldBe("Decide on approvals");

        var system = await Admin.PutAsJsonAsync($"/api/v1/identity/roles/{admin}", new RoleRequest("Admins", null), Ct);
        (system.StatusCode, await CodeAsync(system)).ShouldBe((HttpStatusCode.Conflict, "identity.role_system"));

        var viewer = app.As(await UserAsync("viewer", await RoleAsync("Viewers", IdentityPermissions.Roles.View)), _setup.TenantId);
        (await viewer.PutAsJsonAsync($"/api/v1/identity/roles/{other}", new RoleRequest("Renamed", null), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/roles/{other}", new RoleRequest(FixedName, null), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Copied_users_keep_the_role_and_moved_chosen_users_leave_it_with_audit_entries()
    {
        var (source, a, b) = (await RoleAsync("Source"), await RoleAsync("A"), await RoleAsync("B"));
        var u1 = await UserAsync("u1", source);
        var u2 = await UserAsync("u2", source, a);
        var outsider = await UserAsync("outsider");

        (await TransferAsync(Admin, source, new TransferRoleUsersRequest([a, b], Move: false))).ShouldBe(2);

        (await RoleIdsAsync(u1)).ShouldBe([source, a, b], ignoreOrder: true);
        (await RoleIdsAsync(u2)).ShouldBe([source, a, b], ignoreOrder: true);
        (await RoleIdsAsync(outsider)).ShouldBeEmpty();

        (await TransferAsync(Admin, a, new TransferRoleUsersRequest([b], Move: true, [u1]))).ShouldBe(1);

        (await RoleIdsAsync(u1)).ShouldBe([source, b], ignoreOrder: true);
        (await RoleIdsAsync(u2)).ShouldBe([source, a, b], ignoreOrder: true);
        var audits = await app.InDbAsync(null, db => db.Set<AuditEntry>().IgnoreQueryFilters().Where(e => e.EntityType.StartsWith("IdentityUserRole"))
            .Select(e => new { e.EntityId, e.Action, e.ActorId }).ToListAsync(Ct));
        audits.ShouldContain(e => e.EntityId == $"{u1},{b}" && e.Action == AuditAction.Created && e.ActorId == _setup.AdminUserId);
        audits.ShouldContain(e => e.EntityId == $"{u1},{a}" && e.Action == AuditAction.Deleted && e.ActorId == _setup.AdminUserId);
    }

    [Fact]
    public async Task The_users_of_a_role_are_listed_by_role()
    {
        var role = await RoleAsync("Listed");
        var member = await UserAsync("member", role);
        await UserAsync("other");

        var page = await Admin.GetFromJsonAsync<PagedResult<UserDto>>($"/api/v1/identity/users?roleId={role}&page=1&pageSize=10", Ct);

        page!.Items.Select(u => u.Id).ShouldBe([member]);
    }

    [Fact]
    public async Task Transfers_need_Users_Manage_stay_in_the_tenant_and_refuse_wrong_input()
    {
        var (source, target) = (await RoleAsync("Source"), await RoleAsync("Target"));
        var member = await UserAsync("member", source);
        var stranger = await UserAsync("stranger");
        var foreignRole = await app.InDbAsync(null, db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var role = new Role { Name = "Foreign", NormalizedName = "FOREIGN", TenantId = tenant.Id };
            db.AddRange(tenant, role);
            return Task.FromResult(role.Id);
        });

        var viewer = app.As(await UserAsync("roles", await RoleAsync("Role managers", IdentityPermissions.Roles.Manage, IdentityPermissions.Users.View)), _setup.TenantId);
        (await PostAsync(viewer, source, new TransferRoleUsersRequest([target], false))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PostAsync(Admin, foreignRole, new TransferRoleUsersRequest([target], false))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(Admin, source, new TransferRoleUsersRequest([foreignRole], false))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(Admin, source, new TransferRoleUsersRequest([source], false))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync(Admin, source, new TransferRoleUsersRequest([], false))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync(Admin, source, new TransferRoleUsersRequest([target], false, []))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync(Admin, source, new TransferRoleUsersRequest([target], false, [stranger]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await RoleIdsAsync(member)).ShouldBe([source]);
        (await RoleIdsAsync(stranger)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Managers_move_users_only_within_their_permissions_and_never_administrators()
    {
        var manager = app.As(await UserAsync("manager", await RoleAsync("Managers", IdentityPermissions.Users.Manage, IdentityPermissions.Users.View)), _setup.TenantId);
        var plain = await RoleAsync("Plain");
        var target = await RoleAsync("Target");
        var powerful = await RoleAsync("Powerful", IdentityPermissions.Permissions.Manage);
        var adminRole = (await RolesAsync()).Single(r => r.Name == SystemRoles.Admin).Id;
        var user = await UserAsync("plain", plain);
        var rich = await UserAsync("rich", plain, powerful);

        var toPowerful = await PostAsync(manager, plain, new TransferRoleUsersRequest([powerful], false, [user]));
        (toPowerful.StatusCode, await CodeAsync(toPowerful)).ShouldBe((HttpStatusCode.Forbidden, "identity.grant_restricted"));
        var richUser = await PostAsync(manager, plain, new TransferRoleUsersRequest([target], false));
        (richUser.StatusCode, await CodeAsync(richUser)).ShouldBe((HttpStatusCode.Forbidden, "identity.user_more_permissions"));
        var admins = await PostAsync(manager, adminRole, new TransferRoleUsersRequest([target], false));
        (admins.StatusCode, await CodeAsync(admins)).ShouldBe((HttpStatusCode.Forbidden, "identity.admin_role_restricted"));
        (await TransferAsync(manager, plain, new TransferRoleUsersRequest([target], true, [user]))).ShouldBe(1);

        (await RoleIdsAsync(user)).ShouldBe([target]);
        (await RoleIdsAsync(rich)).ShouldBe([plain, powerful], ignoreOrder: true);
    }

    [Fact]
    public async Task Moving_every_administrator_away_is_refused_copying_them_is_not()
    {
        var target = await RoleAsync("Former admins");
        var adminRole = (await RolesAsync()).Single(r => r.Name == SystemRoles.Admin).Id;

        var moved = await PostAsync(Admin, adminRole, new TransferRoleUsersRequest([target], true));
        (moved.StatusCode, await CodeAsync(moved)).ShouldBe((HttpStatusCode.Conflict, "identity.last_admin"));
        (await TransferAsync(Admin, adminRole, new TransferRoleUsersRequest([target], false))).ShouldBe(1);

        (await RoleIdsAsync(_setup.AdminUserId)).ShouldBe([adminRole, target], ignoreOrder: true);
    }

    private async Task<int> TransferAsync(HttpClient client, Guid role, TransferRoleUsersRequest request)
    {
        var response = await PostAsync(client, role, request);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<int>(Ct);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid role, TransferRoleUsersRequest request) =>
        client.PostAsJsonAsync($"/api/v1/identity/roles/{role}/users/transfer", request, Ct);

    private async Task<RoleDto[]> RolesAsync() => (await Admin.GetFromJsonAsync<RoleDto[]>("/api/v1/identity/roles", Ct))!;

    private async Task<List<Guid>> RoleIdsAsync(Guid userId) =>
        [.. (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{userId}", Ct))!.Roles.Select(r => r.Id)];

    private async Task<Guid> RoleAsync(string name, params string[] permissions)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct);
        response.EnsureSuccessStatusCode();
        var role = await response.Content.ReadFromJsonAsync<Guid>(Ct);
        if (permissions.Length > 0)
        {
            (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        }

        return role;
    }

    private async Task<Guid> UserAsync(string name, params Guid[] roles)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{name}@example.com", "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        var user = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
        if (roles.Length > 0)
        {
            (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest(roles), Ct)).EnsureSuccessStatusCode();
        }

        return user;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();
}
