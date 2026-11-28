using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity.Tests;

/// <summary>
/// Root rules of group-scoped administration: a scoped administrator's own top groups (his memberships without another membership above them) stay as an
/// administrator above him set them up: not moved, deleted or given other roles, and every group stays below the top group it belongs to. Tenant-wide
/// callers (administrators, <c>Identity.Groups.ScopeAll</c>) are unbound; a member of every group is not tenant-wide here.
/// </summary>
public sealed class RootGroupTests(IdentityApp app) : IAsyncLifetime
{
    private const string Locked = "identity.root_group_locked";

    private static readonly string[] ManagerPermissions = [IdentityPermissions.Groups.Manage, IdentityPermissions.Groups.View];

    private SetupResultDto _setup = null!;
    private Guid _p;
    private Guid _s;
    private Guid _c;
    private Guid _p2;
    private Guid _manager;
    private Guid _role;

    public async ValueTask InitializeAsync()
    {
        Scoped(false);
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
        _p = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("P", null));
        _s = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("S", null, _p));
        _c = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("C", null, _p));
        _p2 = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("P2", null));
        await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Other", null));
        _role = await PostAsync<Guid>(Admin, "/api/v1/identity/roles", new RoleRequest("Plain", null));
        _manager = await UserWithAsync("manager@p.test", ManagerPermissions);
        await JoinAsync(_p, _manager);
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
    public async Task Scoped_admin_cannot_move_delete_or_change_roles_of_his_top_group()
    {
        await LockedAsync(await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_p}/parent", new MoveGroupRequest(_s), Ct));
        await LockedAsync(await Manager.DeleteAsync($"/api/v1/identity/groups/{_p}", Ct));
        await LockedAsync(await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_p}/roles", new IdListRequest([_role]), Ct));

        // unchanged roles change nothing; renaming stays his
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_p}/roles", new IdListRequest([]), Ct)).EnsureSuccessStatusCode();
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_p}", new GroupRequest("P renamed", null), Ct)).EnsureSuccessStatusCode();

        var p = (await Admin.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?page=1&pageSize=50", Ct))!.Items.Single(g => g.Id == _p);
        p.Name.ShouldBe("P renamed");
        p.ParentId.ShouldBeNull();
        p.RoleIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sub_groups_stay_below_their_top_group()
    {
        await JoinAsync(_p2, _manager);
        var sub = await PostAsync<Guid>(Manager, "/api/v1/identity/groups", new GroupRequest("Sub", null, _p));

        await LockedAsync(await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{sub}/parent", new MoveGroupRequest(_p2), Ct));
        await LockedAsync(await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_p}/parent", new MoveGroupRequest(_p2), Ct));
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{sub}/parent", new MoveGroupRequest(_c), Ct)).EnsureSuccessStatusCode();
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{sub}/parent", new MoveGroupRequest(_p), Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_membership_below_a_top_group_is_an_ordinary_sub_group()
    {
        var g = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("G", null, _c));
        await JoinAsync(g, _manager);

        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{g}/roles", new IdListRequest([_role]), Ct)).EnsureSuccessStatusCode();
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{g}/parent", new MoveGroupRequest(_s), Ct)).EnsureSuccessStatusCode();
        (await Manager.DeleteAsync($"/api/v1/identity/groups/{g}", Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Member_of_every_group_is_still_bound_by_root_rules()
    {
        // P, P2 and Other: his reach is the whole tenant, his top groups stay locked
        var groups = (await Admin.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?page=1&pageSize=50", Ct))!.Items;
        foreach (var top in groups.Where(g => g.ParentId is null && g.Id != _p))
        {
            await JoinAsync(top.Id, _manager);
        }

        (await Manager.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?page=1&pageSize=50", Ct))!.TotalCount.ShouldBe(groups.Count);
        await LockedAsync(await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_p}/parent", new MoveGroupRequest(_p2), Ct));
        await LockedAsync(await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_p}/parent", new MoveGroupRequest(null), Ct));
        await LockedAsync(await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_s}/parent", new MoveGroupRequest(null), Ct));
        await LockedAsync(await Manager.DeleteAsync($"/api/v1/identity/groups/{_p2}", Ct));
    }

    [Fact]
    public async Task Tenant_wide_admins_keep_moving_top_groups()
    {
        await JoinAsync(_p, _setup.AdminUserId);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/groups/{_p}/parent", new MoveGroupRequest(_p2), Ct)).EnsureSuccessStatusCode();

        var operatorId = await UserWithAsync("operator@acme.test", [.. ManagerPermissions, IdentityPermissions.Groups.ScopeAll]);
        await JoinAsync(_p, operatorId);
        (await app.As(operatorId, _setup.TenantId).PutAsJsonAsync($"/api/v1/identity/groups/{_p}/parent", new MoveGroupRequest(null), Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Group_list_marks_the_callers_top_groups_locked()
    {
        var mine = (await Manager.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?page=1&pageSize=50", Ct))!.Items;
        mine.Single(g => g.Id == _p).Locked.ShouldBeTrue();
        mine.Single(g => g.Id == _s).Locked.ShouldBeFalse();

        var all = (await Admin.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?page=1&pageSize=50", Ct))!.Items;
        all.ShouldAllBe(g => !g.Locked);

        // a member of every group: all his top groups, nothing below them
        foreach (var top in all.Where(g => g.ParentId is null && g.Id != _p))
        {
            await JoinAsync(top.Id, _manager);
        }

        var every = (await Manager.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?page=1&pageSize=50", Ct))!.Items;
        every.Count.ShouldBe(all.Count);
        every.Where(g => g.Locked).Select(g => g.Id).ShouldBe(all.Where(g => g.ParentId is null).Select(g => g.Id), ignoreOrder: true);
    }

    [Fact]
    public async Task A_sub_group_does_not_move_deep_into_another_top_group()
    {
        await JoinAsync(_p2, _manager);
        var mid = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Mid", null, _p2));
        var deep = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Deep", null, mid));

        await LockedAsync(await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_s}/parent", new MoveGroupRequest(deep), Ct));
        (await Manager.PutAsJsonAsync($"/api/v1/identity/groups/{_s}/parent", new MoveGroupRequest(_c), Ct)).EnsureSuccessStatusCode();
    }

    private static async Task LockedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe(Locked);
    }

    private async Task JoinAsync(Guid groupId, params Guid[] userIds) =>
        (await Admin.PostAsJsonAsync($"/api/v1/identity/groups/{groupId}/members/add", new IdListRequest(userIds), Ct)).EnsureSuccessStatusCode();

    private async Task<Guid> UserWithAsync(string email, params string[] permissions)
    {
        var user = await PostAsync<UserDto>(Admin, "/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null));
        var role = await PostAsync<Guid>(Admin, "/api/v1/identity/roles", new RoleRequest("Role " + email, null));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return user.Id;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}
