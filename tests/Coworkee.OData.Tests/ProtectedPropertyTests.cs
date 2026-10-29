using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Search;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.OData.Transfer;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

/// <summary>Gadget.Category is registered with HideUnless(Gadgets.Categories): a viewer with Gadgets.View only must not reach it anywhere.</summary>
public class ProtectedPropertyTests(ODataApp app) : IAsyncLifetime
{
    /// <summary>The OData route the requests go to; <see cref="CamelCaseRoute"/> runs the suite again on a route that renames every property.</summary>
    protected virtual string Route => CoworkeeODataModule.RoutePrefix;

    private HttpClient As(Guid userId, Guid tenantId) => app.As(userId, tenantId, Route);

    private SetupResultDto _setup = null!;
    private Guid _viewer;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(
                new Gadget { Name = "Drill", Category = "Tools", Price = 99, SerialCode = "SN-777", TenantId = _setup.TenantId },
                new Gadget { Name = "Lamp", Category = "Light", Price = 35, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync();
        });
        _viewer = await UserWithAsync(ODataApp.ViewGadgets);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Viewer => As(_viewer, _setup.TenantId);

    [Fact]
    public async Task Lists_leave_the_property_out_and_refuse_queries_on_it()
    {
        var page = await Viewer.GetFromJsonAsync<JsonElement>("/odata/Gadgets?$orderby=Name", Ct);

        page.GetProperty("value")[0].TryGetProperty("Category", out _).ShouldBeFalse();
        page.GetProperty("value")[0].GetProperty("Price").GetDecimal().ShouldBe(99);
        foreach (var query in new[] { "$filter=Category eq 'Tools'", "$orderby=Category", "$select=Name,Category", "$filter=category eq 'Tools'", "$select=name,category" })
        {
            (await Viewer.GetAsync($"/odata/Gadgets?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, query);
        }

        // options without $ are no options: they cannot slip past the checks
        (await Viewer.GetFromJsonAsync<JsonElement>("/odata/Gadgets?filter=Category eq 'Tools'", Ct)).GetProperty("value").GetArrayLength().ShouldBe(2);
        (await Admin.GetFromJsonAsync<JsonElement>("/odata/Gadgets?$filter=Category eq 'Tools'", Ct)).GetProperty("value")[0].GetProperty("Category").GetString().ShouldBe("Tools");
        (await Viewer.GetFromJsonAsync<JsonElement>($"/odata/Gadgets({page.GetProperty("value")[0].GetProperty("Id").GetGuid()})", Ct)).TryGetProperty("Category", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Search_reads_only_properties_the_user_may_read()
    {
        static async Task<int> CountAsync(HttpClient client, string url) =>
            (await client.GetFromJsonAsync<JsonElement>(url, TestContext.Current.CancellationToken)).GetProperty("value").GetArrayLength();

        (await CountAsync(Viewer, "/odata/Gadgets?$search=tools")).ShouldBe(0);
        (await CountAsync(Admin, "/odata/Gadgets?$search=tools")).ShouldBe(1);
        (await CountAsync(Admin, "/odata/Gadgets?$search=SN-777")).ShouldBe(0); // hidden for everyone
        (await CountAsync(Viewer, "/odata/Gadgets?$search=drill")).ShouldBe(1);

        (await Viewer.GetStringAsync("/api/v1/data/Gadgets/export?format=csv&$search=tools", Ct)).ShouldNotContain("Drill");
        (await Admin.GetStringAsync("/api/v1/data/Gadgets/export?format=csv&$search=SN-777", Ct)).ShouldNotContain("Drill");
        (await Viewer.GetStringAsync("/api/v1/data/Gadgets/export?format=csv&$search=drill", Ct)).ShouldContain("Drill");
    }

    [Fact]
    public async Task Facets_leave_the_property_out()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Gadgets");
        request.Headers.Add("Prefer", "odata.include-annotations=\"*\"");

        var page = await (await Viewer.SendAsync(request, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        page.ToString().ShouldNotContain("Tools");
    }

    [Fact]
    public async Task Exports_leave_the_column_out_and_refuse_filters_on_it()
    {
        var csv = await Viewer.GetStringAsync("/api/v1/data/Gadgets/export?format=csv", Ct);

        csv.ShouldContain("Drill");
        csv.ShouldNotContain("Category");
        csv.ShouldNotContain("Tools");
        (await Viewer.GetAsync("/api/v1/data/Gadgets/export?format=csv&$filter=Category eq 'Tools'", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.GetStringAsync("/api/v1/data/Gadgets/export?format=csv", Ct)).ShouldContain("Tools");
    }

    [Fact]
    public async Task The_global_search_does_not_match_on_it()
    {
        (await SearchAsync(Viewer, "tools"))!.Hits.ShouldBeEmpty();
        (await SearchAsync(Viewer, "drill"))!.Hits.Select(h => h.Title).ShouldBe(["Drill"]);
        (await SearchAsync(Admin, "tools"))!.Hits.Select(h => h.Title).ShouldBe(["Drill"]);
    }

    [Fact]
    public async Task The_data_tools_neither_list_nor_return_nor_filter_it()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_viewer, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        (await dispatcher.SendAsync(new ListDataSetsQuery(), Ct)).Value.Single(s => s.Name == "Gadgets").Fields.Keys.ShouldNotContain("Category");
        (await dispatcher.SendAsync(new QueryDataQuery("Gadgets"), Ct)).Value.Rows[0].Keys.ShouldNotContain("Category");
        (await dispatcher.SendAsync(new QueryDataQuery("Gadgets", "Category eq 'Tools'"), Ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await dispatcher.SendAsync(new QueryDataQuery("Gadgets", Select: "Name,Category"), Ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    private Task<GlobalSearchResult?> SearchAsync(HttpClient client, string text) =>
        client.GetFromJsonAsync<GlobalSearchResult>($"/api/v1/search?types=gadgets&q={Uri.EscapeDataString(text)}", Ct);

    private async Task<Guid> UserWithAsync(params string[] permissions)
    {
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"viewer-{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct);
        var role = await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Viewers " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user!.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return user.Id;
    }
}
