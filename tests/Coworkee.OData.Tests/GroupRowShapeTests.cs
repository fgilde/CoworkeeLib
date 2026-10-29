using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;

namespace Coworkee.OData.Tests;

/// <summary>What the groups page reads from the real Groups and Users sets: the roles of a group, no member list, members counted on Users.</summary>
public class GroupRowShapeTests(ODataApp app) : IAsyncLifetime
{
    /// <summary>The OData route the requests go to; <see cref="CamelCaseRoute"/> runs the suite again on a route that renames every property.</summary>
    protected virtual string Route => CoworkeeODataModule.RoutePrefix;

    private HttpClient As(Guid userId, Guid tenantId) => app.As(userId, tenantId, Route);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Groups_carry_their_roles_and_no_member_list_and_users_count_the_members()
    {
        var group = await IdAsync(await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("Crew", null), Ct));
        var (viewer, editor) = (await RoleAsync("Viewer"), await RoleAsync("Editor"));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/groups/{group}/roles", new IdListRequest([viewer, editor]), Ct)).EnsureSuccessStatusCode();
        var member = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("m@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
        (await Admin.PostAsJsonAsync($"/api/v1/identity/groups/{group}/members/add", new IdListRequest([member, _setup.AdminUserId]), Ct)).EnsureSuccessStatusCode();

        var raw = await Admin.GetFromJsonAsync<JsonElement>($"/odata/Groups?$filter=Id eq {group}", Ct);
        raw.GetProperty("value")[0].TryGetProperty("Members", out _).ShouldBeFalse();
        var row = raw.GetProperty("value")[0].Deserialize<GroupRow>(Web)!;
        row.Roles.Select(r => r.RoleId).ShouldBe([viewer, editor], ignoreOrder: true);
        (await Admin.GetAsync($"/odata/Groups?$filter=Id eq {group}&$expand=Members", Ct)).IsSuccessStatusCode.ShouldBeFalse();
        // the guard of the navigation scope: the hidden member list cannot be filtered on, the roles can be selected and filtered
        (await Admin.GetAsync($"/odata/Groups?$filter=Members/any(m: m/UserId eq {member})", Ct)).IsSuccessStatusCode.ShouldBeFalse();
        var selected = await Admin.GetFromJsonAsync<JsonElement>($"/odata/Groups?$filter=Roles/any(r: r/RoleId eq {viewer})&$select=Id,Roles", Ct);
        selected.GetProperty("value").EnumerateArray().Select(g => g.GetProperty("Id").GetGuid()).ShouldBe([group]);
        selected.GetProperty("value")[0].GetProperty("Roles").GetArrayLength().ShouldBe(2);

        var count = await Admin.GetFromJsonAsync<JsonElement>($"/odata/Users?$filter=Groups/any(m: m/GroupId eq {group})&$select=Id&$top=0&$count=true", Ct);
        count.GetProperty("@odata.count").GetInt32().ShouldBe(2);
        var users = await Admin.GetFromJsonAsync<JsonElement>($"/odata/Users?$filter=Id eq {member}", Ct);
        users.GetProperty("value")[0].GetProperty("Groups").EnumerateArray().Select(m => m.GetProperty("GroupId").GetGuid()).ShouldBe([group]);
    }

    private async Task<Guid> RoleAsync(string name) => await IdAsync(await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct));

    private static async Task<Guid> IdAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }
}
