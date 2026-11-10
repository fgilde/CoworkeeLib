using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.Tests;

public sealed class AdminODataTests(IdentityApp app) : IAsyncLifetime
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
    public async Task Users_come_as_odata_without_secrets_with_an_active_facet()
    {
        (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("eve@acme.test", "Passw0rd!x", "Eve", null), Ct)).EnsureSuccessStatusCode();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Users?$orderby=Email&$count=true");
        request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");

        var body = await (await Admin.SendAsync(request, Ct)).Content.ReadAsStringAsync(Ct);

        var page = JsonDocument.Parse(body).RootElement;
        page.GetProperty("@odata.count").GetInt32().ShouldBe(2);
        page.GetProperty("value").EnumerateArray().Select(u => u.GetProperty("Email").GetString()).ShouldBe(["admin@acme.test", "eve@acme.test"]);
        body.ShouldNotContain("PasswordHash");
        body.ShouldNotContain("SecurityStamp");
        body.ShouldContain("Active");
    }

    [Fact]
    public async Task Roles_and_names_of_a_page_of_users_come_in_one_call()
    {
        var roles = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users/roles", new IdListRequest([_setup.AdminUserId, Guid.CreateVersion7()]), Ct))
            .Content.ReadFromJsonAsync<Dictionary<Guid, RoleRefDto[]>>(Ct))!;
        var names = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users/names", new IdListRequest([_setup.AdminUserId]), Ct))
            .Content.ReadFromJsonAsync<Dictionary<Guid, string>>(Ct))!;

        roles.Keys.ShouldBe([_setup.AdminUserId]);
        roles[_setup.AdminUserId].Select(r => r.Name).ShouldContain(Domain.SystemRoles.Admin);
        names[_setup.AdminUserId].ShouldContain("Ada");
    }

    [Fact]
    public async Task System_roles_and_the_own_tenant_roles_are_listed()
    {
        var page = await Admin.GetFromJsonAsync<JsonElement>("/odata/Roles?$orderby=Name", Ct);

        page.GetProperty("value").EnumerateArray().Select(r => r.GetProperty("Name").GetString()).ShouldBe([Domain.SystemRoles.Admin, Domain.SystemRoles.User]);
    }

    [Fact]
    public async Task Sessions_filter_and_show_by_their_end_as_the_sessions_page_asks()
    {
        // the page's filter for active sessions; the end was missing from the model (no public setter) and the server refused it
        var response = await Admin.GetAsync("/odata/UserSessions?$filter=EndedAt eq null and ExpiresAt gt 2026-01-01T00:00:00Z&$select=EndedAt,EndReason&$count=true", Ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }
}
