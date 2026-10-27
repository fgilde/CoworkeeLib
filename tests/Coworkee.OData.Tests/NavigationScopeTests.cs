using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.OData.Transfer;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

/// <summary>Navigations show only the rows their target set lists for the user: $expand, $filter through them, $count, exports and the data tools.</summary>
public sealed class NavigationScopeTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _open;
    private Guid _featuresSecret;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        (_open, _featuresSecret) = await app.InDbAsync(_setup.TenantId, async db =>
        {
            var tenant = _setup.TenantId;
            var secret = new Bin { Name = "Secret", Size = 9, TenantId = tenant };
            var plain = new Bin
            {
                Name = "Plain", Size = 1, TenantId = tenant,
                Labels = [new BinLabel { Text = "Fragile", TenantId = tenant }, new BinLabel { Text = "Secret", TenantId = tenant }],
            };
            var open = new Shelf { Name = "Open", TenantId = tenant, Bins = [plain, secret], Lockers = [new Locker { Name = "L1", TenantId = tenant }] };
            var other = new Shelf { Name = "Other", TenantId = tenant };
            db.AddRange(open, other);
            await db.SaveChangesAsync();
            (open.Featured, other.Featured) = (plain, secret);
            await db.SaveChangesAsync();
            return (open.Id, other.Id);
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private Task<JsonElement> GetAsync(string url) => Admin.GetFromJsonAsync<JsonElement>(url, Ct);

    private static string[] Names(JsonElement rows, string property = "Name") =>
        [.. rows.EnumerateArray().Select(r => r.GetProperty(property).GetString()!).Order()];

    [Fact]
    public async Task An_expanded_collection_leaves_out_the_rows_its_filter_hides()
    {
        var page = await GetAsync("/odata/Shelves?$filter=Name eq 'Open'&$expand=Bins($count=true)");

        var shelf = page.GetProperty("value")[0];
        Names(shelf.GetProperty("Bins")).ShouldBe(["Plain"]);
        shelf.GetProperty("Bins@odata.count").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task An_expanded_reference_to_a_hidden_row_is_null()
    {
        var page = await GetAsync("/odata/Shelves?$expand=Featured&$orderby=Name");

        var shelves = page.GetProperty("value");
        shelves[0].GetProperty("Name").GetString().ShouldBe("Open");
        shelves[0].GetProperty("Featured").GetProperty("Name").GetString().ShouldBe("Plain");
        shelves[1].GetProperty("Featured").ValueKind.ShouldBe(JsonValueKind.Null);
        (await GetAsync($"/odata/Shelves({_featuresSecret})?$expand=Featured")).GetProperty("Featured").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Nested_expands_apply_the_filters_of_every_level()
    {
        // labels have no set of their own: the EDM shows them as a complex collection, read with $select
        var shelf = await GetAsync($"/odata/Shelves({_open})?$expand=Bins($select=Name,Labels)");

        var bins = shelf.GetProperty("Bins");
        Names(bins).ShouldBe(["Plain"]);
        Names(bins[0].GetProperty("Labels"), "Text").ShouldBe(["Fragile"]);
    }

    [Fact]
    public async Task Filters_and_orders_through_a_navigation_see_only_visible_rows()
    {
        (await GetAsync("/odata/Shelves?$filter=Bins/any(b: b/Name eq 'Secret')&$count=true")).GetProperty("@odata.count").GetInt32().ShouldBe(0);
        (await GetAsync("/odata/Shelves?$filter=Bins/any(b: b/Size gt 0)&$count=true")).GetProperty("@odata.count").GetInt32().ShouldBe(1);
        (await GetAsync("/odata/Shelves?$filter=Bins/all(b: b/Size lt 5)&$count=true")).GetProperty("@odata.count").GetInt32().ShouldBe(2);
        (await GetAsync("/odata/Shelves?$filter=Featured/Name eq 'Secret'")).GetProperty("value").GetArrayLength().ShouldBe(0);
        (await GetAsync("/odata/Shelves?$filter=Bins/$count eq 1")).GetProperty("value").GetArrayLength().ShouldBe(1);
        (await GetAsync("/odata/Bins?$filter=Labels/any(l: l/Text eq 'Secret')")).GetProperty("value").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Expanding_a_set_without_its_permission_is_forbidden()
    {
        var viewer = app.As(await UserWithAsync(ODataApp.ViewGadgets), _setup.TenantId);

        (await viewer.GetAsync("/odata/Shelves", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var expand in new[] { "Bins", "Featured", "Bins($select=Labels)" })
        {
            var response = await viewer.GetAsync($"/odata/Shelves?$expand={expand}", Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, expand);
            (await response.Content.ReadAsStringAsync(Ct)).ShouldContain(expand[..3]);
        }

        // a filter through the navigation sees no row of the set
        (await viewer.GetFromJsonAsync<JsonElement>("/odata/Shelves?$filter=Bins/any()", Ct)).GetProperty("value").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Protected_properties_of_an_expanded_set_stay_hidden()
    {
        var viewer = app.As(await UserWithAsync(ODataApp.ViewGadgets, ODataApp.ViewBins), _setup.TenantId);

        var shelf = await viewer.GetFromJsonAsync<JsonElement>($"/odata/Shelves({_open})?$expand=Bins", Ct);
        shelf.GetProperty("Bins")[0].TryGetProperty("Size", out _).ShouldBeFalse();
        foreach (var query in new[] { "$expand=Bins($select=Size)", "$filter=Bins/any(b: b/Size gt 0)", "$expand=Bins($orderby=Size)" })
        {
            (await viewer.GetAsync($"/odata/Shelves?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, query);
        }
    }

    [Fact]
    public async Task A_filter_that_is_no_plain_where_refuses_the_expand()
    {
        var response = await Admin.GetAsync("/odata/Shelves?$expand=Lockers", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("row filters of Locker");
        (await GetAsync("/odata/Lockers")).GetProperty("value").GetArrayLength().ShouldBe(1);
        (await GetAsync("/odata/Shelves?$filter=Lockers/any()")).GetProperty("value").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Exports_and_the_data_tools_filter_through_visible_rows_only()
    {
        (await Admin.GetStringAsync("/api/v1/data/Shelves/export?format=csv&$filter=Bins/any(b: b/Name eq 'Secret')", Ct)).ShouldNotContain("Open");
        (await Admin.GetStringAsync("/api/v1/data/Shelves/export?format=csv&$filter=Bins/any(b: b/Name eq 'Plain')", Ct)).ShouldContain("Open");

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var page = (await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new QueryDataQuery("Shelves", "Featured/Name eq 'Secret'"), Ct)).Value;
        page.Total.ShouldBe(0);
    }

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
