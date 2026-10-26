using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Tests;

public sealed class GroupTreeQueryTests(IdentityApp app) : IAsyncLifetime
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
    public async Task Children_are_listed_by_name_with_counts()
    {
        var b = await CreateAsync("B");
        var a = await CreateAsync("A");
        var a2 = await CreateAsync("A2", a);
        await CreateAsync("A1", a);
        await CreateAsync("A2x", a2);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/groups/{a}/members", new IdListRequest([_setup.AdminUserId]), Ct)).EnsureSuccessStatusCode();

        var roots = await NodesAsync("children");
        var children = await NodesAsync($"children?parentId={a}");

        roots.Select(n => (n.Name, n.ChildCount, n.MemberCount, n.Code)).ShouldBe([("A", 2, 1, "00002"), ("B", 0, 0, "00001")]);
        roots[1].Id.ShouldBe(b);
        children.Select(n => (n.Name, n.ChildCount, n.ParentId, n.Code)).ShouldBe([("A1", 0, a, "00002.00002"), ("A2", 1, a, "00002.00001")]);
    }

    [Fact]
    public async Task Search_returns_matches_with_their_ancestors()
    {
        var customer = await CreateAsync("Customer A");
        var hamburg = await CreateAsync("Hamburg", customer);
        await CreateAsync("Port", hamburg);
        await CreateAsync("Berlin", customer);
        await CreateAsync("Other");

        var nodes = await NodesAsync("tree?search=HAM");

        nodes.Select(n => n.Name).ShouldBe(["Customer A", "Hamburg"]);
        nodes[0].ChildCount.ShouldBe(2);
        nodes[1].ParentId.ShouldBe(customer);
        (await NodesAsync("tree?search=port")).Select(n => n.Name).ShouldBe(["Customer A", "Hamburg", "Port"]);
        (await NodesAsync("tree?search=")).ShouldBeEmpty();
        (await NodesAsync("tree?search=nothing")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_is_capped_at_200_matches()
    {
        await app.InDbAsync(_setup.TenantId, db =>
        {
            db.AddRange(Enumerable.Range(1, 230).Select(i => new UserGroup { Name = $"Team {i}", Code = $"{i:D5}" }));
            return Task.FromResult(0);
        });

        var nodes = await NodesAsync("tree?search=team");

        nodes.Count.ShouldBe(200);
        nodes[0].Code.ShouldBe("00001");
    }

    [Fact]
    public async Task Search_resolves_more_ancestor_codes_than_a_command_has_parameters()
    {
        // 200 distinct chains of 16 levels: 3200 prefixes, above the 2100 parameters of a SQL Server command
        await app.InDbAsync(_setup.TenantId, db =>
        {
            foreach (var chain in Enumerable.Range(1, 200))
            {
                Guid? parent = null;
                var code = "";
                for (var level = 1; level <= 16; level++)
                {
                    code += (level == 1 ? "" : ".") + (level == 1 ? $"{chain:D5}" : "00001");
                    var group = new UserGroup { Name = level == 16 ? $"Leaf {chain}" : $"Node {chain}-{level}", Code = code, ParentId = parent };
                    db.Add(group);
                    parent = group.Id;
                }
            }

            return Task.FromResult(0);
        });

        var nodes = await NodesAsync("tree?search=leaf");

        nodes.Count.ShouldBe(3200);
        nodes.Select(n => n.Code).Distinct().Count().ShouldBe(3200);
        nodes.Count(n => n.Name.StartsWith("Leaf")).ShouldBe(200);
    }

    [Fact]
    public async Task Tree_queries_stay_in_the_tenant()
    {
        var mine = await CreateAsync("Shared name");
        await CreateAsync("Child", mine);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/groups/{mine}/members", new IdListRequest([_setup.AdminUserId]), Ct)).EnsureSuccessStatusCode();
        var (tenant, admin) = await app.InDbAsync(null, async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var user = new User { TenantId = tenant.Id, UserName = "eve@other.test", Email = "eve@other.test", NormalizedUserName = "EVE@OTHER.TEST", NormalizedEmail = "EVE@OTHER.TEST" };
            var colleague = new User { TenantId = tenant.Id, UserName = "fay@other.test", Email = "fay@other.test", NormalizedUserName = "FAY@OTHER.TEST", NormalizedEmail = "FAY@OTHER.TEST" };
            var role = await db.Set<Role>().IgnoreQueryFilters().FirstAsync(r => r.IsSystem && r.Name == SystemRoles.Admin);
            var group = new UserGroup { Name = "Shared name", TenantId = tenant.Id, Code = "00001" };
            db.AddRange(tenant, user, colleague, new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id }, group,
                new UserGroupMember { GroupId = group.Id, UserId = user.Id }, new UserGroupMember { GroupId = group.Id, UserId = colleague.Id });
            return (tenant.Id, user.Id);
        });
        var other = app.As(admin, tenant);

        (await NodesAsync("children", other)).Select(n => n.Code).ShouldBe(["00001"]);
        (await NodesAsync("children", other)).Single().ChildCount.ShouldBe(0);
        (await NodesAsync("children", other)).Single().MemberCount.ShouldBe(2);
        (await NodesAsync("children")).Single().MemberCount.ShouldBe(1);
        (await NodesAsync("tree?search=shared", other)).Single().MemberCount.ShouldBe(2);
        (await NodesAsync($"children?parentId={mine}", other)).ShouldBeEmpty();
        (await NodesAsync("tree?search=child", other)).ShouldBeEmpty();
        (await NodesAsync("tree?search=shared", other)).Select(n => n.Id).ShouldNotContain(mine);
        (await NodesAsync("tree?search=shared")).Single().Id.ShouldBe(mine);
    }

    [Fact]
    public async Task OData_groups_expose_parent_and_code()
    {
        var a = await CreateAsync("A");
        await CreateAsync("A1", a);

        var page = await Admin.GetFromJsonAsync<JsonElement>("/odata/Groups?$select=Name,ParentId,Code&$orderby=Code", Ct);

        var rows = page.GetProperty("value").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("Code").GetString()).ShouldBe(["00001", "00001.00001"]);
        rows[0].GetProperty("ParentId").ValueKind.ShouldBe(JsonValueKind.Null);
        rows[1].GetProperty("ParentId").GetGuid().ShouldBe(a);
    }

    private async Task<IReadOnlyList<GroupNodeDto>> NodesAsync(string route, HttpClient? client = null)
    {
        var response = await (client ?? Admin).GetAsync($"/api/v1/identity/groups/{route}", Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<GroupNodeDto>>(Ct))!;
    }

    private async Task<Guid> CreateAsync(string name, Guid? parent = null)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest(name, null, parent), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }
}
