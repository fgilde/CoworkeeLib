using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Groups;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Tests;

public sealed class GroupTreeTests(IdentityApp app) : IAsyncLifetime
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
    public async Task Roots_and_children_get_path_codes()
    {
        var a = await CreateAsync("A");
        await CreateAsync("B");
        var a1 = await CreateAsync("A1", a);
        await CreateAsync("A2", a);

        var groups = (await Admin.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?pageSize=50", Ct))!.Items;

        groups.ToDictionary(g => g.Name, g => g.Code).ShouldBe(new Dictionary<string, string?>
        {
            ["A"] = "00001", ["B"] = "00002", ["A1"] = "00001.00001", ["A2"] = "00001.00002",
        }, ignoreOrder: true);
        groups.Single(g => g.Id == a1).ParentId.ShouldBe(a);
    }

    [Fact]
    public async Task Same_name_is_allowed_for_cousins_but_not_siblings()
    {
        var a = await CreateAsync("A");
        var b = await CreateAsync("B");
        await CreateAsync("X", a);
        await CreateAsync("X", b);

        await ShouldConflictAsync(new GroupRequest("X", null, a));
        await ShouldConflictAsync(new GroupRequest("A", null));
    }

    [Fact]
    public async Task Unknown_or_foreign_parent_is_rejected()
    {
        var foreign = await app.InDbAsync(null, async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var group = new UserGroup { Name = "Foreign", TenantId = tenant.Id, Code = "00001" };
            db.Add(tenant);
            db.Add(group);
            await Task.CompletedTask;
            return group.Id;
        });

        foreach (var parent in new[] { foreign, Guid.CreateVersion7() })
        {
            var response = await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("Child", null, parent), Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("identity.group_parent_not_found");
        }
    }

    [Fact]
    public async Task Depth_is_limited_to_16()
    {
        Guid? parent = null;
        for (var level = 1; level <= 16; level++)
        {
            parent = await CreateAsync($"L{level}", parent);
        }

        (await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("L17", null, parent), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Missing_codes_are_filled_in_name_order()
    {
        var kept = await CreateAsync("Kept");
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(new UserGroup { Name = "c" }, new UserGroup { Name = "a" }, new UserGroup { Name = "b" });
            await Task.CompletedTask;
            return 0;
        });

        await app.InDbAsync(null, async db =>
        {
            await GroupCodes.FillMissingAsync(db, Ct);
            return 0;
        });

        var codes = await app.InDbAsync(_setup.TenantId, db => db.Set<UserGroup>().ToDictionaryAsync(g => g.Name, g => g.Code, Ct));
        codes.ShouldBe(new Dictionary<string, string?> { ["Kept"] = "00001", ["a"] = "00002", ["b"] = "00003", ["c"] = "00004" }, ignoreOrder: true);
    }

    [Fact]
    public async Task Move_rewrites_the_codes_of_the_subtree()
    {
        var a = await CreateAsync("A");
        var a1 = await CreateAsync("A1", a);
        var a1a = await CreateAsync("A1a", a1);
        var b = await CreateAsync("B");

        (await MoveAsync(a1, b)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var groups = await GroupsAsync();
        groups["A1"].Code.ShouldBe("00002.00001");
        groups["A1"].ParentId.ShouldBe(b);
        groups["A1a"].Code.ShouldBe("00002.00001.00001");
        groups["A1a"].ParentId.ShouldBe(a1);
        groups["A"].Code.ShouldBe("00001");
    }

    [Fact]
    public async Task Move_to_root_appends_a_root_segment()
    {
        var a = await CreateAsync("A");
        var a1 = await CreateAsync("A1", a);
        await CreateAsync("B");

        (await MoveAsync(a1, null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var groups = await GroupsAsync();
        groups["A1"].Code.ShouldBe("00003");
        groups["A1"].ParentId.ShouldBeNull();
    }

    [Fact]
    public async Task Move_into_own_subtree_is_rejected()
    {
        var a = await CreateAsync("A");
        var a1 = await CreateAsync("A1", a);
        var a1a = await CreateAsync("A1a", a1);

        foreach (var target in new[] { a1a, a })
        {
            var response = await MoveAsync(a, target);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("identity.group_move_cycle");
        }

        (await GroupsAsync())["A"].Code.ShouldBe("00001");
    }

    [Fact]
    public async Task Move_rejects_sibling_name_clash_and_too_deep_targets()
    {
        var a = await CreateAsync("A");
        var b = await CreateAsync("B");
        var x = await CreateAsync("X", a);
        await CreateAsync("X", b);
        (await MoveAsync(x, b)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        Guid? deep = null;
        for (var level = 1; level <= 15; level++)
        {
            deep = await CreateAsync($"L{level}", deep);
        }

        var branch = await CreateAsync("Branch");
        await CreateAsync("Leaf", branch);
        (await MoveAsync(branch, deep)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MoveAsync(Guid.CreateVersion7(), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MoveAsync(branch, Guid.CreateVersion7())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var leaf = (await Admin.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?pageSize=100", Ct))!.Items.Single(g => g.Name == "Leaf");
        leaf.Code.ShouldBe("00004.00001");
    }

    [Fact]
    public async Task Delete_removes_the_subtree_with_its_grants()
    {
        var a = await CreateAsync("A");
        var a1 = await CreateAsync("A1", a);
        var a1a = await CreateAsync("A1a", a1);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/groups/{a1}/members", new IdListRequest([_setup.AdminUserId]), Ct)).EnsureSuccessStatusCode();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.Add(new PermissionGrant { Name = IdentityPermissions.Users.View, ProviderType = PermissionProviderType.Group, ProviderKey = a1a, TenantId = _setup.TenantId });
            db.Add(new GroupSelection { UserId = _setup.AdminUserId, GroupId = a1a });
            await Task.CompletedTask;
            return 0;
        });

        (await Admin.DeleteAsync($"/api/v1/identity/groups/{a}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Admin.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?pageSize=50", Ct))!.Items.ShouldBeEmpty();
        var left = await app.InDbAsync(_setup.TenantId, async db => new
        {
            Grants = await db.Set<PermissionGrant>().IgnoreQueryFilters().CountAsync(g => g.ProviderType == PermissionProviderType.Group, Ct),
            Selections = await db.Set<GroupSelection>().CountAsync(Ct),
            Members = await db.Set<UserGroupMember>().CountAsync(Ct),
        });
        left.Grants.ShouldBe(0);
        left.Selections.ShouldBe(0);
        left.Members.ShouldBe(0);
    }

    private Task<HttpResponseMessage> MoveAsync(Guid id, Guid? parent) =>
        Admin.PutAsJsonAsync($"/api/v1/identity/groups/{id}/parent", new MoveGroupRequest(parent), Ct);

    private async Task<Dictionary<string, GroupDto>> GroupsAsync() =>
        (await Admin.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups?pageSize=100", Ct))!.Items.ToDictionary(g => g.Name);

    private async Task<Guid> CreateAsync(string name, Guid? parent = null)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest(name, null, parent), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }

    private async Task ShouldConflictAsync(GroupRequest request)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/groups", request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("identity.group_name_taken");
    }
}
