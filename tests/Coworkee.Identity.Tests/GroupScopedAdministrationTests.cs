using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Search;
using Coworkee.Core.Security;
using Coworkee.Identity.Users;
using Coworkee.Identity.Users.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity.Tests;

/// <summary>
/// Group-scoped administration (<c>Coworkee:Identity:GroupScopedAdministration</c>): customers A and B in one organisation. A's manager reaches A, its
/// sub-group Hamburg and their members; B's users, users of no group and B's groups are unknown to him, users shared with B are changed only by someone
/// reaching all their groups, and the top level, the default group and roles need the whole organisation. ScopeAll, administrators and the system reach all.
/// </summary>
public sealed class GroupScopedAdministrationTests(IdentityApp app) : IAsyncLifetime
{
    private const string OtherGroups = "identity.user_other_groups";
    private const string AllGroups = "identity.all_groups_required";

    private static readonly string[] ManagerPermissions =
    [
        IdentityPermissions.Users.Manage, IdentityPermissions.Roles.Manage, IdentityPermissions.Permissions.Manage, IdentityPermissions.Groups.Manage,
        IdentityPermissions.ResourcePermissions.Manage, IdentityPermissions.Users.Impersonate, ApiKeyPermissions.Manage,
    ];

    private SetupResultDto _setup = null!;
    private Guid _a;
    private Guid _hamburg;
    private Guid _b;
    private Guid _manager;
    private Guid _alice;
    private Guid _bob;
    private Guid _shared;
    private Guid _loose;
    private Guid _plainRole;

    public async ValueTask InitializeAsync()
    {
        Scoped(false);
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
        _a = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Customer A", null));
        _hamburg = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Hamburg", null, _a));
        _b = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Customer B", null));
        _plainRole = await RoleWithAsync("Plain", ApiKeyPermissions.Create);
        _manager = await UserWithAsync("manager@a.test", ManagerPermissions);
        _alice = await UserAsync("alice@a.test");
        _bob = await UserAsync("bob@b.test");
        _shared = await UserAsync("shared@ab.test");
        _loose = await UserAsync("loose@acme.test");
        await JoinAsync(_a, _manager, _shared);
        await JoinAsync(_hamburg, _alice);
        await JoinAsync(_b, _bob, _shared);
        Scoped(true);
    }

    public ValueTask DisposeAsync()
    {
        Scoped(false);
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Manager => app.As(_manager, _setup.TenantId);

    private void Scoped(bool on) => app.App.Services.GetRequiredService<IOptions<IdentityAdministrationOptions>>().Value.GroupScopedAdministration = on;

    [Fact]
    public async Task Lists_counts_and_lookups_hold_only_the_users_and_groups_in_reach()
    {
        var page = (await Manager.GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/identity/users?page=1&pageSize=50", Ct))!;
        page.Items.Select(u => u.Id).ShouldBe([_alice, _manager, _shared], ignoreOrder: true);
        page.TotalCount.ShouldBe(3);

        var odata = await Manager.GetFromJsonAsync<JsonElement>("/odata/Users?$count=true&$select=Id", Ct);
        odata.GetProperty("@odata.count").GetInt32().ShouldBe(3);
        Ids(odata).ShouldBe([_alice, _manager, _shared], ignoreOrder: true);
        Ids(await Manager.GetFromJsonAsync<JsonElement>("/odata/Groups?$select=Id", Ct)).ShouldBe([_a, _hamburg], ignoreOrder: true);
        (await Manager.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?page=1&pageSize=50", Ct))!.Items.Select(g => g.Id).ShouldBe([_a, _hamburg], ignoreOrder: true);

        // through navigations: the members of a group out of reach stay hidden
        Ids(await Manager.GetFromJsonAsync<JsonElement>($"/odata/Users?$filter=Groups/any(m: m/GroupId eq {_b})&$select=Id", Ct)).ShouldBe([_shared]);

        var roles = (await (await Manager.PostAsJsonAsync("/api/v1/identity/users/roles", new IdListRequest([_alice, _bob, _loose]), Ct)).Content
            .ReadFromJsonAsync<Dictionary<Guid, RoleRefDto[]>>(Ct))!;
        roles.Keys.ShouldBe([_alice]);

        var granted = (await Manager.GetFromJsonAsync<PagedResult<UserDto>>(
            $"/api/v1/identity/users/granted?permission={ApiKeyPermissions.Create}&page=1&pageSize=50", Ct))!;
        granted.Items.Select(u => u.Id).ShouldBe([_alice, _manager, _shared], ignoreOrder: true);
        granted.TotalCount.ShouldBe(3);
        (await Manager.GetAsync($"/api/v1/identity/users/granted?permission={ApiKeyPermissions.Create}&groupId={_b}&page=1&pageSize=50", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        (await Manager.GetStringAsync("/api/v1/search?q=alice", Ct)).ShouldContain("alice@a.test");
        (await Manager.GetStringAsync("/api/v1/search?q=bob", Ct)).ShouldNotContain("bob@b.test");
        (await Manager.GetStringAsync("/api/v1/search?q=loose", Ct)).ShouldNotContain("loose@acme.test");
        var groups = await Manager.GetStringAsync("/api/v1/search?q=customer", Ct);
        groups.ShouldContain("Customer A");
        groups.ShouldNotContain("Customer B");
        (await Admin.GetStringAsync("/api/v1/search?q=bob", Ct)).ShouldContain("bob@b.test");
    }

    [Fact]
    public async Task The_group_tree_of_a_scoped_manager_starts_at_his_groups()
    {
        var roots = (await Manager.GetFromJsonAsync<List<GroupNodeDto>>("/api/v1/identity/groups/children", Ct))!;
        roots.Select(n => n.Id).ShouldBe([_a]);
        (await Manager.GetFromJsonAsync<List<GroupNodeDto>>($"/api/v1/identity/groups/children?parentId={_a}", Ct))!.Select(n => n.Id).ShouldBe([_hamburg]);
        (await Manager.GetFromJsonAsync<List<GroupNodeDto>>($"/api/v1/identity/groups/children?parentId={_b}", Ct))!.ShouldBeEmpty();
        (await Manager.GetFromJsonAsync<List<GroupNodeDto>>("/api/v1/identity/groups/tree?search=customer", Ct))!.Select(n => n.Id).ShouldBe([_a]);

        // a manager of Hamburg only: his tree starts there, without the ancestor out of reach
        var localId = await UserWithAsync("local@a.test", IdentityPermissions.Groups.Manage);
        await JoinAsync(_hamburg, localId);
        var local = app.As(localId, _setup.TenantId);
        (await local.GetFromJsonAsync<List<GroupNodeDto>>("/api/v1/identity/groups/children", Ct))!.Select(n => n.Id).ShouldBe([_hamburg]);
        (await local.GetFromJsonAsync<List<GroupNodeDto>>("/api/v1/identity/groups/tree?search=hamburg", Ct))!.Select(n => n.Id).ShouldBe([_hamburg]);
    }

    [Fact]
    public async Task Users_and_groups_out_of_reach_answer_as_unknown()
    {
        foreach (var user in new[] { _bob, _loose, _setup.AdminUserId })
        {
            (await Manager.GetAsync($"/api/v1/identity/users/{user}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await Manager.GetAsync($"/api/v1/identity/users/{user}/permissions", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await Manager.GetAsync($"/api/v1/identity/permissions/grants/User/{user}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await Manager.PostAsync($"/api/v1/identity/users/{user}/impersonation", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await Manager.GetAsync($"/api/v1/identity/permissions/grants/Group/{_b}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_b}", new GroupRequest("Renamed", null), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.DeleteAsync($"/api/v1/identity/groups/{_b}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_b}/members", new IdListRequest([]), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.PostAsJsonAsync($"/api/v1/identity/groups/{_b}/members/remove", new IdListRequest([_bob]), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.PostAsJsonAsync($"/api/v1/identity/groups/{_b}/members/move", new MoveMembersRequest(_hamburg), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_b}/owners", new IdListRequest([]), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_b}/roles", new IdListRequest([]), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_b}/parent", new MoveGroupRequest(_a), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Group/{_b}", new NameListRequest([]), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // B's groups and users stay as they were
        (await Admin.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?page=1&pageSize=50", Ct))!.Items.Single(g => g.Id == _b)
            .MemberIds.ShouldBe([_bob, _shared], ignoreOrder: true);
    }

    public static TheoryData<string> Operations =>
    [
        "password", "two-factor", "passkeys", "login", "avatar", "groups", "roles", "update", "unlock", "lock", "sign-out", "delete", "grants", "api-key",
        "group-remove",
    ];

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_scoped_manager_changes_users_of_his_groups_only(string operation)
    {
        (await RunAsync(operation, Manager, _bob)).StatusCode.ShouldBe(operation == "group-remove" ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound);
        // the administrator put B's user into Hamburg first: shared now; the user of no group is A's then
        var onLoose = await RunAsync(operation, Manager, _loose);
        if (operation == "group-remove")
        {
            onLoose.IsSuccessStatusCode.ShouldBeTrue();
        }
        else
        {
            onLoose.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var onShared = await RunAsync(operation, Manager, _shared);
        onShared.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(onShared)).ShouldBe(OtherGroups);

        (await RunAsync(operation, Manager, _alice)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task ScopeAll_administrators_and_the_option_off_reach_everyone(string operation)
    {
        var operatorId = await UserWithAsync("operator@acme.test", [.. ManagerPermissions, IdentityPermissions.Groups.ScopeAll]);
        (await RunAsync(operation, app.As(operatorId, _setup.TenantId), _bob)).IsSuccessStatusCode.ShouldBeTrue();
        (await RunAsync(operation, Admin, _shared)).IsSuccessStatusCode.ShouldBeTrue();

        Scoped(false);
        (await RunAsync(operation, Manager, _loose)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Users_join_a_group_in_reach_only_when_they_belong_to_no_group_outside_it()
    {
        var add = $"/api/v1/identity/groups/{_hamburg}/members/add";
        var unknown = await Manager.PostAsJsonAsync(add, new IdListRequest([_bob]), Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_hamburg}/owners", new IdListRequest([_alice, _bob]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_hamburg}/members", new IdListRequest([_alice, _bob]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var shared = await Manager.PostAsJsonAsync(add, new IdListRequest([_shared]), Ct);
        shared.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(shared)).ShouldBe(OtherGroups);

        // a user of no group comes into reach
        (await Manager.PostAsJsonAsync(add, new IdListRequest([_loose]), Ct)).EnsureSuccessStatusCode();
        (await Manager.GetAsync($"/api/v1/identity/users/{_loose}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // members of B and of the shared user's groups never moved
        (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{_bob}", Ct))!.Groups.Select(g => g.Id).ShouldBe([_b]);
        (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{_shared}", Ct))!.Groups.Select(g => g.Id).ShouldBe([_a, _b], ignoreOrder: true);
    }

    [Fact]
    public async Task A_shared_user_shows_only_the_groups_in_reach_and_keeps_the_others()
    {
        // alice is in Hamburg and in the default group, which belongs to nobody
        var everyone = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Everyone", null));
        (await Admin.PutAsJsonAsync("/api/v1/identity/groups/default", new DefaultGroupDto(everyone), Ct)).EnsureSuccessStatusCode();
        await JoinAsync(everyone, _alice, _manager);

        (await Manager.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{_shared}", Ct))!.Groups.Select(g => g.Id).ShouldBe([_a]);
        (await Manager.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{_alice}", Ct))!.Groups.Select(g => g.Id).ShouldBe([_hamburg]);

        // the list replaces the memberships in reach; the default group stays
        (await Manager.PutAsJsonAsync($"/api/v1/identity/users/{_alice}/groups", new IdListRequest([_a]), Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{_alice}", Ct))!.Groups.Select(g => g.Id).ShouldBe([_a, everyone], ignoreOrder: true);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/users/{_alice}/groups", new IdListRequest([_b]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/users/{_alice}/groups", new IdListRequest([everyone]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_default_group_neither_opens_the_reach_nor_counts_against_it()
    {
        var everyone = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Everyone", null));
        var newcomers = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Newcomers", null, everyone));
        (await Admin.PutAsJsonAsync("/api/v1/identity/groups/default", new DefaultGroupDto(everyone), Ct)).EnsureSuccessStatusCode();
        await JoinAsync(everyone, _manager, _bob, _alice);
        var fresh = await UserAsync("fresh@acme.test");
        var newcomer = await UserAsync("newcomer@acme.test");
        await JoinAsync(newcomers, newcomer);

        // in the default group with everyone else: still only A
        (await Manager.GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/identity/users?page=1&pageSize=50", Ct))!.Items.Select(u => u.Id)
            .ShouldBe([_alice, _manager, _shared], ignoreOrder: true);
        Ids(await Manager.GetFromJsonAsync<JsonElement>("/odata/Groups?$select=Id", Ct)).ShouldBe([_a, _hamburg], ignoreOrder: true);
        (await Manager.GetAsync($"/api/v1/identity/users/{newcomer}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // alice is in A and the default group: changed by A's manager
        (await RunAsync("update", Manager, _alice)).IsSuccessStatusCode.ShouldBeTrue();

        // a new user only in the default group joins A
        (await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{fresh}", Ct))!.Groups.Select(g => g.Id).ShouldBe([everyone]);
        (await Manager.PostAsJsonAsync($"/api/v1/identity/groups/{_hamburg}/members/add", new IdListRequest([fresh]), Ct)).EnsureSuccessStatusCode();

        // the default group belongs to the organisation
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{everyone}/roles", new IdListRequest([]), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.PutAsJsonAsync("/api/v1/identity/groups/default", new DefaultGroupDto(_hamburg), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Groups_change_inside_the_reach_and_the_top_level_needs_every_group()
    {
        // roles of the organisation are given to users in reach
        var role = await RoleWithAsync("A role", ApiKeyPermissions.Create);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/users/{_alice}/roles", new IdListRequest([_plainRole, role]), Ct)).EnsureSuccessStatusCode();

        var root = await Manager.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("Customer C", null), Ct);
        root.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(root)).ShouldBe(AllGroups);
        (await Manager.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("Berlin", null, _b), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var berlin = await PostAsync<Guid>(Manager, "/api/v1/identity/groups", new GroupRequest("Berlin", null, _a));

        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{berlin}/parent", new MoveGroupRequest(null), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{berlin}/parent", new MoveGroupRequest(_b), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{berlin}/parent", new MoveGroupRequest(_hamburg), Ct)).EnsureSuccessStatusCode();
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_hamburg}/parent", new MoveGroupRequest(null), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await Manager.PostAsJsonAsync($"/api/v1/identity/groups/{_hamburg}/members/move", new MoveMembersRequest(_b), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.PostAsJsonAsync($"/api/v1/identity/groups/{_hamburg}/members/move", new MoveMembersRequest(berlin), Ct)).EnsureSuccessStatusCode();
        (await Manager.DeleteAsync($"/api/v1/identity/groups/{berlin}", Ct)).EnsureSuccessStatusCode();

        // A's group roles and grants reach the shared user's work for B
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_hamburg}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        (await Manager.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Group/{_hamburg}", new NameListRequest([ApiKeyPermissions.Create]), Ct)).EnsureSuccessStatusCode();
        var roles = await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_a}/roles", new IdListRequest([role]), Ct);
        roles.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(roles)).ShouldBe(OtherGroups);
        (await Manager.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Group/{_a}", new NameListRequest([ApiKeyPermissions.Create]), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Manager.DeleteAsync($"/api/v1/identity/groups/{_a}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // roles reach users everywhere
        foreach (var response in new[]
                 {
                     await Manager.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Mine", null), Ct),
                     await Manager.PutAsJsonAsync($"/api/v1/identity/roles/{role}", new RoleRequest("Renamed", null), Ct),
                     await Manager.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([]), Ct),
                     await Manager.DeleteAsync($"/api/v1/identity/roles/{role}", Ct),
                     await Manager.PutAsJsonAsync("/api/v1/identity/groups/default", new DefaultGroupDto(_hamburg), Ct),
                 })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await CodeAsync(response)).ShouldBe(AllGroups);
        }
    }

    [Fact]
    public async Task The_group_import_creates_below_groups_in_reach_only()
    {
        var import = new Groups.ImportGroup(new Groups.GroupImportRow("Bremen", null, "Customer A/Hamburg"));
        (await SendAsManagerAsync(import)).IsSuccess.ShouldBeTrue();
        (await SendAsManagerAsync(new Groups.ImportGroup(new Groups.GroupImportRow("Berlin", null, "Customer B")))).Error!.Code.ShouldBe("identity.group_parent_path_unknown");
        (await SendAsManagerAsync(new Groups.ImportGroup(new Groups.GroupImportRow("Nowhere", null, "Customer Z")))).Error!.Code.ShouldBe("identity.group_parent_path_unknown");
        (await SendAsManagerAsync(new Groups.ImportGroup(new Groups.GroupImportRow("Customer C", null, null)))).Error!.Code.ShouldBe(AllGroups);
    }

    [Fact]
    public async Task A_group_made_earlier_in_the_same_request_is_in_reach()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_manager, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        // the reach is read once per scope
        (await dispatcher.SendAsync(new Groups.GetGroups(new PageRequest()), Ct)).Value!.TotalCount.ShouldBe(2);
        var berlin = (await dispatcher.SendAsync(new Groups.CreateGroup(new GroupRequest("Berlin", null, _a)), Ct)).Value;

        (await dispatcher.SendAsync(new Groups.AddGroupMembers(berlin, [_alice]), Ct)).IsSuccess.ShouldBeTrue();
        (await dispatcher.SendAsync(new Groups.CreateGroup(new GroupRequest("Mitte", null, berlin)), Ct)).IsSuccess.ShouldBeTrue();
        (await dispatcher.SendAsync(new Groups.AddGroupMembers(_b, [_alice]), Ct)).Error!.Code.ShouldBe("identity.group_not_found");
    }

    [Fact]
    public async Task New_users_of_a_scoped_manager_join_one_of_his_groups()
    {
        var none = await Manager.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("none@a.test", "Passw0rd!x", null, null), Ct);
        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Manager.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("b@a.test", "Passw0rd!x", null, null, GroupIds: [_b]), Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);

        var user = await PostAsync<UserDto>(Manager, "/api/v1/identity/users", new CreateUserRequest("new@a.test", "Passw0rd!x", null, null, GroupIds: [_hamburg]));

        (await Manager.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{user.Id}", Ct))!.Groups.Select(g => g.Id).ShouldBe([_hamburg]);
        (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("plain@acme.test", "Passw0rd!x", null, null), Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Resource_grants_name_principals_in_reach_only()
    {
        var folder = Guid.CreateVersion7();
        var url = $"/api/v1/identity/resource-permissions/Folder/{folder}";
        var role = await RoleWithAsync("Readers", ApiKeyPermissions.Create);
        var toBob = await PostAsync<Guid>(Admin, url, new GrantResourcePermissionRequest(PrincipalType.User, _bob, role));
        var toB = await PostAsync<Guid>(Admin, url, new GrantResourcePermissionRequest(PrincipalType.Group, _b, role));

        (await Manager.PostAsJsonAsync(url, new GrantResourcePermissionRequest(PrincipalType.User, _loose, role), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Manager.PostAsJsonAsync(url, new GrantResourcePermissionRequest(PrincipalType.Group, _b, role), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var toAlice = await PostAsync<Guid>(Manager, url, new GrantResourcePermissionRequest(PrincipalType.User, _alice, role));
        (await Manager.PostAsJsonAsync(url, new GrantResourcePermissionRequest(PrincipalType.Group, _hamburg, role), Ct)).EnsureSuccessStatusCode();

        (await Manager.GetFromJsonAsync<List<ResourcePermissionDto>>(url, Ct))!.Select(p => p.PrincipalId).ShouldBe([_alice, _hamburg], ignoreOrder: true);
        (await Admin.GetFromJsonAsync<List<ResourcePermissionDto>>(url, Ct))!.Count.ShouldBe(4);
        (await Manager.DeleteAsync($"{url}/{toBob}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.DeleteAsync($"{url}/{toB}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Manager.DeleteAsync($"{url}/{toAlice}", Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Api_keys_of_users_out_of_reach_are_not_listed()
    {
        await KeyOfAsync(_bob);
        var alices = await KeyOfAsync(_alice);

        Ids(await Manager.GetFromJsonAsync<JsonElement>("/odata/ApiKeys?$select=Id", Ct)).ShouldBe([alices]);
        (await Admin.GetFromJsonAsync<JsonElement>("/odata/ApiKeys?$select=Id", Ct)).GetProperty("value").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task An_administrator_of_every_group_reaches_the_whole_organisation()
    {
        await JoinAsync(_b, _manager);

        (await Manager.GetAsync($"/api/v1/identity/users/{_loose}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RunAsync("update", Manager, _shared)).IsSuccessStatusCode.ShouldBeTrue();
        (await Manager.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("Customer C", null), Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_system_passes_and_other_organisations_stay_apart()
    {
        // what the app decided: the system acts on any user
        using (CurrentUserScope.Begin(new ImpersonatedUser(_manager, _setup.TenantId)))
        {
            await using var scope = app.App.Services.CreateAsyncScope();
            var system = scope.ServiceProvider.GetRequiredService<ISystemDispatcher>();
            (await system.SendAsync(new SetUserGroups(_bob, [_b, _hamburg]), [IdentityPermissions.Groups.Manage], Ct)).IsSuccess.ShouldBeTrue();
        }

        var (tenant, manager) = await app.InDbAsync(null, db =>
        {
            var tenant = new Domain.Tenant { Name = "Other", Identifier = "other" };
            var user = new Domain.User { UserName = "manager@other.test", Email = "manager@other.test", TenantId = tenant.Id };
            db.AddRange(tenant, user);
            db.AddRange(ManagerPermissions.Append(IdentityPermissions.Groups.ScopeAll)
                .Select(name => new Domain.PermissionGrant { Name = name, ProviderType = PermissionProviderType.User, ProviderKey = user.Id, TenantId = tenant.Id }));
            return Task.FromResult((tenant.Id, user.Id));
        });
        var other = app.As(manager, tenant);

        (await other.GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/identity/users?page=1&pageSize=50", Ct))!.Items.Select(u => u.Id).ShouldBe([manager]);
        (await other.GetAsync($"/api/v1/identity/users/{_alice}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RunAsync("update", other, _alice)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<Coworkee.Core.Results.Result<Guid>> SendAsManagerAsync(Groups.ImportGroup command)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_manager, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, Ct);
    }

    private async Task<HttpResponseMessage> RunAsync(string operation, HttpClient caller, Guid target)
    {
        var users = $"/api/v1/identity/users/{target}";
        return operation switch
        {
            "password" => await caller.PutAsJsonAsync($"{users}/password", new SetPasswordRequest("N3w#Passw0rd!x", false), Ct),
            "two-factor" => await caller.PostAsync($"{users}/two-factor/reset", null, Ct),
            "passkeys" => await caller.PostAsync($"{users}/passkeys/reset", null, Ct),
            "login" => await caller.DeleteAsync($"{users}/logins?provider=Test&key=abc", Ct),
            "avatar" => await caller.PutAsJsonAsync($"{users}/avatar", new SetAvatarRequest(null), Ct),
            "groups" => await caller.PutAsJsonAsync($"{users}/groups", new IdListRequest([]), Ct),
            "roles" => await caller.PutAsJsonAsync($"{users}/roles", new IdListRequest([_plainRole]), Ct),
            "update" => await caller.PutAsJsonAsync(users, new UpdateUserRequest("New", null, true), Ct),
            "unlock" => await caller.PostAsync($"{users}/unlock", null, Ct),
            "lock" => await caller.PostAsJsonAsync($"{users}/lock", new LockUserRequest(null), Ct),
            "sign-out" => await caller.PostAsync($"{users}/sign-out", null, Ct),
            "delete" => await caller.DeleteAsync(users, Ct),
            "grants" => await caller.PutAsJsonAsync($"/api/v1/identity/permissions/grants/User/{target}", new NameListRequest([]), Ct),
            "api-key" => await caller.PostAsync($"/api/v1/identity/api-keys/{await KeyOfAsync(target)}/revoke", null, Ct),
            "group-remove" => await RemoveFromHamburgAsync(caller, target),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    // the administrator puts the user into Hamburg first: B's user is then shared, a user of no group A's
    private async Task<HttpResponseMessage> RemoveFromHamburgAsync(HttpClient caller, Guid target)
    {
        await JoinAsync(_hamburg, target);
        return await caller.PostAsJsonAsync($"/api/v1/identity/groups/{_hamburg}/members/remove", new IdListRequest([target]), Ct);
    }

    private async Task<Guid> KeyOfAsync(Guid userId)
    {
        var request = new CreateApiKeyRequest("key", null, DateTimeOffset.UtcNow.AddDays(1), [ApiKeyPermissions.Create]);
        return (await PostAsync<CreatedApiKeyDto>(app.As(userId, _setup.TenantId), "/api/v1/identity/api-keys", request)).Key.Id;
    }

    private async Task JoinAsync(Guid groupId, params Guid[] userIds) =>
        (await Admin.PostAsJsonAsync($"/api/v1/identity/groups/{groupId}/members/add", new IdListRequest(userIds), Ct)).EnsureSuccessStatusCode();

    private async Task<Guid> RoleWithAsync(string name, params string[] permissions)
    {
        var role = await PostAsync<Guid>(Admin, "/api/v1/identity/roles", new RoleRequest(name, null));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        return role;
    }

    private async Task<Guid> UserAsync(string email)
    {
        var user = await PostAsync<UserDto>(Admin, "/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null, RoleIds: [_plainRole]));
        return user.Id;
    }

    private async Task<Guid> UserWithAsync(string email, params string[] permissions)
    {
        var user = await PostAsync<UserDto>(Admin, "/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null));
        var role = await RoleWithAsync("Role " + email, permissions);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return user.Id;
    }

    private static IEnumerable<Guid> Ids(JsonElement page) => page.GetProperty("value").EnumerateArray().Select(r => r.GetProperty("Id").GetGuid()).ToList();

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();
}
