using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Query;
using Coworkee.Identity.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity.Tests;

/// <summary>
/// Data filter administration under group-scoped administration: a scoped administrator reads the Group filters of his reach and writes those of the
/// groups below his own top groups (the filters of his top groups are set above him); Role filters need the whole tenant; unknown keys answer like
/// unreached groups for him and as a validation error for tenant-wide callers.
/// </summary>
public sealed class DataFilterReachTests(IdentityApp app) : IAsyncLifetime
{
    private const string Root = "/api/v1/data-filters";
    private const string Forbidden = "data_filters.forbidden";

    private static readonly string Query = QueryJson.Serialize(new QueryGroup { Children = [new QueryRule { Field = "Name", Operator = QueryOperator.NotEqual, Value = "x" }] });

    private SetupResultDto _setup = null!;
    private Guid _p;
    private Guid _s;
    private Guid _q;
    private Guid _qFilter;
    private Guid _manager;

    public async ValueTask InitializeAsync()
    {
        Scoped(false);
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
        _p = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("P", null));
        _s = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("S", null, _p));
        _q = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Q", null));
        await SaveAsync(Admin, DataFilterLayer.Group, _p);
        await SaveAsync(Admin, DataFilterLayer.Group, _s);
        _qFilter = await SaveAsync(Admin, DataFilterLayer.Group, _q);
        _manager = await UserWithAsync("manager@p.test", DataFilterPermissions.ManageGroups, IdentityPermissions.Groups.View);
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
    public async Task Scoped_admin_edits_sub_group_filters_and_reads_his_own()
    {
        (await Manager.PutAsJsonAsync(Root, Save(DataFilterLayer.Group, _s), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await RefusedAsync(await Manager.PutAsJsonAsync(Root, Save(DataFilterLayer.Group, _p), CoworkeeJson.Web, Ct));

        var listed = (await Manager.GetFromJsonAsync<DataFilterDto[]>($"{Root}?layer=Group", CoworkeeJson.Web, Ct))!;
        listed.Select(f => f.LayerKey).ShouldBe([_p, _s], ignoreOrder: true);

        (await Manager.PostAsJsonAsync($"{Root}/preview", Preview(DataFilterLayer.Group, _s), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await RefusedAsync(await Manager.PostAsJsonAsync($"{Root}/preview", Preview(DataFilterLayer.Group, _p), CoworkeeJson.Web, Ct));
        await RefusedAsync(await Manager.PostAsJsonAsync($"{Root}/preview", Preview(DataFilterLayer.Group, null), CoworkeeJson.Web, Ct));

        // his top group's filter is not his to delete; a sub-group's is
        var mine = listed.Single(f => f.LayerKey == _p).Id;
        await RefusedAsync(await Manager.DeleteAsync($"{Root}/{mine}", Ct));
        (await Manager.DeleteAsync($"{Root}/{listed.Single(f => f.LayerKey == _s).Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Scoped_admin_cannot_touch_unreached_or_unknown_groups_and_cannot_tell_them_apart()
    {
        var unreached = await Manager.PutAsJsonAsync(Root, Save(DataFilterLayer.Group, _q), CoworkeeJson.Web, Ct);
        var unknown = await Manager.PutAsJsonAsync(Root, Save(DataFilterLayer.Group, Guid.NewGuid()), CoworkeeJson.Web, Ct);

        var (code, title) = await RefusedAsync(unreached);
        (await RefusedAsync(unknown)).ShouldBe((code, title));
        (await RefusedAsync(await Manager.PostAsJsonAsync($"{Root}/preview", Preview(DataFilterLayer.Group, Guid.NewGuid()), CoworkeeJson.Web, Ct))).ShouldBe((code, title));
        (await Manager.DeleteAsync($"{Root}/{_qFilter}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Member_of_every_group_still_reads_his_top_group_filters_only()
    {
        await JoinAsync(_q, _manager);

        await RefusedAsync(await Manager.PutAsJsonAsync(Root, Save(DataFilterLayer.Group, _q), CoworkeeJson.Web, Ct));
        await RefusedAsync(await Manager.PutAsJsonAsync(Root, Save(DataFilterLayer.Group, _p), CoworkeeJson.Web, Ct));
        (await Manager.PutAsJsonAsync(Root, Save(DataFilterLayer.Group, _s), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Manager.GetFromJsonAsync<DataFilterDto[]>(Root, CoworkeeJson.Web, Ct))!.Select(f => f.LayerKey).ShouldBe([_p, _s, _q], ignoreOrder: true);
        await RefusedAsync(await Manager.DeleteAsync($"{Root}/{_qFilter}", Ct));
    }

    [Fact]
    public async Task Default_group_only_admin_sees_no_group_filters()
    {
        Scoped(false);
        var everyone = await PostAsync<Guid>(Admin, "/api/v1/identity/groups", new GroupRequest("Everyone", null));
        (await Admin.PutAsJsonAsync("/api/v1/identity/groups/default", new DefaultGroupDto(everyone), Ct)).EnsureSuccessStatusCode();
        var lonely = await UserWithAsync("lonely@acme.test", DataFilterPermissions.ManageGroups, IdentityPermissions.Groups.View);
        await JoinAsync(everyone, lonely);
        Scoped(true);
        var client = app.As(lonely, _setup.TenantId);

        var response = await client.GetAsync(Root, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<DataFilterDto[]>(CoworkeeJson.Web, Ct))!.ShouldBeEmpty();
        foreach (var group in new[] { _p, _s, _q, everyone })
        {
            await RefusedAsync(await client.PutAsJsonAsync(Root, Save(DataFilterLayer.Group, group), CoworkeeJson.Web, Ct));
        }
    }

    [Fact]
    public async Task Role_filters_need_the_whole_tenant()
    {
        Scoped(false);
        var role = await PostAsync<Guid>(Admin, "/api/v1/identity/roles", new RoleRequest("Filtered", null));
        var holder = await UserWithAsync("roles@p.test", DataFilterPermissions.ManageRoles, IdentityPermissions.Roles.View);
        await JoinAsync(_p, holder);
        Scoped(true);
        var client = app.As(holder, _setup.TenantId);

        await RefusedAsync(await client.GetAsync($"{Root}?layer=Role", Ct));
        await RefusedAsync(await client.PutAsJsonAsync(Root, Save(DataFilterLayer.Role, role), CoworkeeJson.Web, Ct));
        await RefusedAsync(await client.PostAsJsonAsync($"{Root}/preview", Preview(DataFilterLayer.Role, role), CoworkeeJson.Web, Ct));

        var saved = await SaveAsync(Admin, DataFilterLayer.Role, role);
        await RefusedAsync(await client.DeleteAsync($"{Root}/{saved}", Ct));
        (await Admin.GetFromJsonAsync<DataFilterDto[]>($"{Root}?layer=Role", CoworkeeJson.Web, Ct))!.ShouldHaveSingleItem().Id.ShouldBe(saved);
        (await Admin.DeleteAsync($"{Root}/{saved}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Tenant_wide_admin_gets_a_validation_error_for_unknown_keys()
    {
        foreach (var layer in new[] { DataFilterLayer.Group, DataFilterLayer.Role })
        {
            var response = await Admin.PutAsJsonAsync(Root, Save(layer, Guid.NewGuid()), CoworkeeJson.Web, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("layerKey", out _).ShouldBeTrue();
        }

        (await Admin.PostAsJsonAsync($"{Root}/preview", Preview(DataFilterLayer.Group, Guid.NewGuid()), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // the reach of a tenant-wide admin is every group
        (await Admin.PutAsJsonAsync(Root, Save(DataFilterLayer.Group, _q), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Admin.GetFromJsonAsync<DataFilterDto[]>($"{Root}?layer=Group", CoworkeeJson.Web, Ct))!.Length.ShouldBe(3);
    }

    private static SaveDataFilterRequest Save(DataFilterLayer layer, Guid? key) => new("Groups", layer, key, Query);

    private static PreviewDataFilterRequest Preview(DataFilterLayer layer, Guid? key) => new("Groups", layer, key, Query);

    private static async Task<Guid> SaveAsync(HttpClient client, DataFilterLayer layer, Guid key)
    {
        var response = await client.PutAsJsonAsync(Root, Save(layer, key), CoworkeeJson.Web, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DataFilterDto>(CoworkeeJson.Web, Ct))!.Id;
    }

    private static async Task<(string? Code, string? Title)> RefusedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var code = body.GetProperty("code").GetString();
        code.ShouldBe(Forbidden);
        return (code, body.GetProperty("title").GetString());
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
