using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

/// <summary>
/// Routes that name properties otherwise: <c>rt/odata</c> (lower camel case, Parcel dates aliased) and <c>alias/odata</c> (protected members and a
/// navigation aliased). The names round trip through every option and the output; the rules follow the CLR member whatever its name.
/// </summary>
public sealed class RouteProfileTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _viewer;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            var tenant = _setup.TenantId;
            db.AddRange(
                new Gadget { Name = "Drill", Category = "Tools", Price = 99, SerialCode = "SN-777", TenantId = tenant },
                new Gadget { Name = "Lamp", Category = "Light", Price = 35, TenantId = tenant });
            var plain = new Bin { Name = "Plain", Size = 1, TenantId = tenant };
            var secret = new Bin { Name = "Secret", Size = 9, TenantId = tenant };
            var open = new Shelf { Name = "Open", TenantId = tenant, Bins = [plain, secret] };
            var other = new Shelf { Name = "Other", TenantId = tenant };
            db.AddRange(open, other, new Sticker { Text = "Fragile", Weight = 3, TenantId = tenant });
            await db.SaveChangesAsync();
            (open.Featured, other.Featured) = (plain, secret);
            return await db.SaveChangesAsync();
        });
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            var parcels = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
            parcels.AddRange(
                new Parcel { Name = "Early", DateCreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new Parcel { Name = "Late", Owner = "Zeta", DateCreatedUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) });
            await parcels.SaveChangesAsync(Ct);
        }

        _viewer = await UserWithAsync(ODataApp.ViewGadgets);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Viewer => app.As(_viewer, _setup.TenantId);

    private async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task Lower_camel_case_names_round_trip_through_every_option_and_the_output()
    {
        var page = await GetAsync(Admin, "/rt/odata/Gadgets?$filter=price gt 50&$orderby=name desc&$select=name,price&$count=true");

        page.GetProperty("@odata.count").GetInt32().ShouldBe(1);
        var row = page.GetProperty("value")[0];
        row.EnumerateObject().Select(p => p.Name).ShouldBe(["name", "price"]);
        row.GetProperty("name").GetString().ShouldBe("Drill");
        (await GetAsync(Admin, "/rt/odata/Gadgets?$orderby=name")).GetProperty("value")[0].EnumerateObject().Select(p => p.Name).ShouldContain("createdAt");

        var shelves = await GetAsync(Admin, "/rt/odata/Shelves?$expand=featured($select=name)&$orderby=name&$filter=featured/name ne null or featured eq null");
        shelves.GetProperty("value")[0].GetProperty("featured").GetProperty("name").GetString().ShouldBe("Plain");
        shelves.GetProperty("value")[1].GetProperty("featured").ValueKind.ShouldBe(JsonValueKind.Null, "the secret bin stays hidden");

        (await GetAsync(Admin, "/rt/odata/Countries?$select=isoCode")).GetProperty("value").ToString().ShouldNotContain("IsoCode");
    }

    [Fact]
    public async Task An_alias_replaces_the_clr_name_on_its_route_only()
    {
        var page = await GetAsync(Admin, "/rt/odata/Parcels?$filter=month(creationTime) gt 3&$orderby=creationTime desc&$select=name,creationTime");

        var row = page.GetProperty("value").EnumerateArray().Single();
        row.GetProperty("name").GetString().ShouldBe("Late");
        row.GetProperty("creationTime").GetString().ShouldStartWith("2026-06-01T"); // a DateTime column: providers read it back with other kinds
        (await GetAsync(Admin, "/rt/odata/Parcels?$top=1")).GetProperty("value")[0].TryGetProperty("lastModificationTime", out _).ShouldBeTrue();
        (await Admin.GetAsync("/rt/odata/Parcels?$filter=month(dateCreatedUtc) gt 3", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetAsync(Admin, "/odata/Parcels?$filter=month(DateCreatedUtc) gt 3")).GetProperty("value").GetArrayLength().ShouldBe(1);

        // an alias on an interface reaches every type that implements it, a navigation alias $expand
        (await GetAsync(Admin, "/alias/odata/Stickers")).GetProperty("value")[0].TryGetProperty("Author", out _).ShouldBeTrue();
        (await GetAsync(Admin, "/alias/odata/Shelves?$expand=Highlight($select=Capacity)&$orderby=Name")).GetProperty("value")[0]
            .GetProperty("Highlight").GetProperty("Capacity").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task An_aliased_protected_property_is_refused_and_left_out_as_under_its_own_name()
    {
        var rows = (await GetAsync(Viewer, "/alias/odata/Gadgets?$orderby=Name")).GetProperty("value");
        rows[0].TryGetProperty("Kind", out _).ShouldBeFalse();
        rows[0].TryGetProperty("Category", out _).ShouldBeFalse();
        foreach (var query in new[] { "$filter=Kind eq 'Tools'", "$orderby=Kind", "$select=Name,Kind", "$filter=kind eq 'Tools'", "$compute=Kind as K&$select=K" })
        {
            (await Viewer.GetAsync($"/alias/odata/Gadgets?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, query);
        }

        // the CLR name is unknown on the route, for everyone; $search does not look in it either
        (await Admin.GetAsync("/alias/odata/Gadgets?$filter=Category eq 'Tools'", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetAsync(Viewer, "/alias/odata/Gadgets?$search=tools&$count=true")).GetProperty("@odata.count").GetInt32().ShouldBe(0);
        (await GetAsync(Admin, "/alias/odata/Gadgets?$filter=Kind eq 'Tools'")).GetProperty("value")[0].GetProperty("Kind").GetString().ShouldBe("Tools");

        // a protected member of a base class, aliased through an interface; a protected property of a navigation target, aliased
        (await Viewer.GetAsync("/alias/odata/Stickers?$filter=Author eq null", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync(Viewer, "/alias/odata/Stickers")).GetProperty("value")[0].TryGetProperty("Author", out _).ShouldBeFalse();
        var binViewer = app.As(await UserWithAsync(ODataApp.ViewGadgets, ODataApp.ViewBins), _setup.TenantId);
        foreach (var query in new[] { "$expand=Highlight($select=Capacity)", "$filter=Highlight/Capacity gt 0", "$orderby=Highlight/Capacity", "$filter=Bins/any(b: b/Capacity gt 0)" })
        {
            (await binViewer.GetAsync($"/alias/odata/Shelves?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, query);
        }

        (await GetAsync(binViewer, "/alias/odata/Shelves?$expand=Highlight&$orderby=Name")).GetProperty("value")[0].GetProperty("Highlight").TryGetProperty("Capacity", out _).ShouldBeFalse();
        (await Viewer.GetAsync("/alias/odata/Shelves?$expand=Highlight", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Facets_come_in_the_names_of_the_route_and_its_filters_take_them_back()
    {
        var facets = await FacetsAsync(Admin, "/alias/odata/Gadgets");
        var kind = facets.Single(f => f.Key == "category");
        kind.Field.ShouldBe("Kind");
        kind.ValuePath.ShouldBe("Kind");
        var tools = kind.Options.Single(o => o.Label == "Tools").OData;
        tools.ShouldBe("Kind eq 'Tools'");
        (await GetAsync(Admin, $"/alias/odata/Gadgets?$filter={Uri.EscapeDataString(tools)}")).GetProperty("value").GetArrayLength().ShouldBe(1);
        (await FacetsAsync(Viewer, "/alias/odata/Gadgets")).ShouldNotContain(f => f.Key == "category");

        // a value path through an aliased navigation into an aliased property
        var depth = (await FacetsAsync(Admin, "/alias/odata/Shelves")).Single(f => f.Key == "depth");
        depth.ValuePath.ShouldBe("Highlight/Capacity");
        (await FacetsAsync(Admin, "/rt/odata/Shelves")).Single(f => f.Key == "featured").ValuePath.ShouldBe("it/featured/name");

        // the facet context is read in the route's names, and checked like the query
        (await FacetsAsync(Admin, "/alias/odata/Gadgets", new FacetContextDto(null, new Dictionary<string, string> { ["category"] = "Kind eq 'Tools'" })))
            .Single(f => f.Key == "category").Options.Sum(o => o.Count).ShouldBe(2);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/alias/odata/Gadgets");
        request.Headers.Add(FacetContextDto.Header, new FacetContextDto("Kind eq 'Tools'", new Dictionary<string, string>()).Encode());
        (await Viewer.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_wildcard_prefer_builds_facets_where_the_route_wants_them()
    {
        foreach (var (route, built) in new[] { ("odata", true), ("rt/odata", true), ("alias/odata", false) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{route}/Gadgets");
            request.Headers.Add("Prefer", "odata.include-annotations=\"*\"");
            var page = await (await Admin.SendAsync(request, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
            page.TryGetProperty("@cn.facets", out _).ShouldBe(built, route);
        }
    }

    [Fact]
    public async Task Search_looks_in_the_fields_of_the_global_search_only()
    {
        // Parcels search Name globally; Owner is a string the list shows, but no search field
        (await GetAsync(Admin, "/odata/Parcels?$search=zeta")).GetProperty("value").GetArrayLength().ShouldBe(0);
        (await GetAsync(Admin, "/rt/odata/Parcels?$search=late")).GetProperty("value").GetArrayLength().ShouldBe(1);
        (await Admin.GetStringAsync("/api/v1/data/Parcels/export?format=csv&$search=zeta", Ct)).ShouldNotContain("Late");
    }

    [Fact]
    public async Task Names_match_only_in_the_routes_case_when_case_insensitivity_is_off()
    {
        await using var strict = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:CaseInsensitive"] = "false" });
        var client = strict.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);

        (await client.GetAsync("/rt/odata/Gadgets?$filter=name eq 'Drill'&$orderby=price&$select=name", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var query in new[] { "$filter=Name eq 'Drill'", "$orderby=Price", "$select=Name" })
        {
            (await client.GetAsync($"/rt/odata/Gadgets?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
        }
    }

    [Fact]
    public async Task An_alias_with_the_name_of_a_hidden_member_reads_the_aliased_member_only()
    {
        // hidden/odata calls Name "SerialCode"; the hidden SerialCode stays out of reach
        var rows = (await GetAsync(Admin, "/hidden/odata/Gadgets?$orderby=SerialCode")).GetProperty("value");
        rows.EnumerateArray().Select(r => r.GetProperty("SerialCode").GetString()).ShouldBe(["Drill", "Lamp"]);
        rows.ToString().ShouldNotContain("SN-777");
        (await GetAsync(Admin, "/hidden/odata/Gadgets?$filter=SerialCode eq 'SN-777'")).GetProperty("value").GetArrayLength().ShouldBe(0);
        (await GetAsync(Admin, "/hidden/odata/Gadgets?$search=777")).GetProperty("value").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_facet_filter_with_the_clr_name_of_an_aliased_member_is_invalid_on_the_alias_route()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/alias/odata/Gadgets");
        request.Headers.Add(FacetContextDto.Header, new FacetContextDto("Category eq 'Tools'", new Dictionary<string, string>()).Encode());
        (await Admin.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var group = new HttpRequestMessage(HttpMethod.Get, "/alias/odata/Gadgets");
        group.Headers.Add(FacetContextDto.Header, new FacetContextDto(null, new Dictionary<string, string> { ["category"] = "Category eq 'Tools'" }).Encode());
        (await Admin.SendAsync(group, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void A_route_needs_a_prefix()
    {
        foreach (var prefix in new[] { "", " ", "/", "//" })
        {
            Should.Throw<ArgumentException>(() => new ServiceCollection().AddCoworkeeODataRoute(prefix, p => p.CamelCase()), prefix);
        }
    }

    [Theory]
    [InlineData("Featured/Size gt 0 and Bins/any(b: b/Size gt 1)", "Highlight/Capacity gt 0 and Bins/any(b: b/Capacity gt 1)")]
    [InlineData("Bins/any(name: name/Name eq 'Size') or Name eq 'Featured/Size'", "Bins/any(name: name/Name eq 'Size') or Name eq 'Featured/Size'")]
    [InlineData("Featured/Coworkee.OData.Tests.Bin/Size ge 2", "Highlight/Coworkee.OData.Tests.Bin/Capacity ge 2")]
    [InlineData("Bins/all(x: x/Labels/any(l: l/Text ne null) and x/Size lt 9) and contains(Name, 'size')", "Bins/all(x: x/Labels/any(l: l/Text ne null) and x/Capacity lt 9) and contains(Name, 'size')")]
    [InlineData("Featured eq null or FeaturedId eq 0197a000-0000-7000-8000-00000000000a", "Highlight eq null or FeaturedId eq 0197a000-0000-7000-8000-00000000000a")]
    public void Facet_filters_are_translated_through_lambdas_and_casts(string clr, string route)
    {
        var model = app.App.Services.GetRequiredService<ODataModels>().Of("alias/odata");
        var root = (Microsoft.OData.Edm.IEdmStructuredType)model.FindDeclaredType(typeof(Shelf).FullName);

        ODataNames.Filter(model, root, clr).ShouldBe(route);
        ODataQueryGuard.Options(app.App.Services, typeof(Shelf), Microsoft.AspNetCore.Http.QueryString.Create("$filter", route), "alias/odata").Filter.FilterClause.ShouldNotBeNull();
    }

    [Fact]
    public void An_alias_that_gives_two_members_one_name_is_refused()
    {
        var profile = new ODataRoutes().Add("twice/odata").Alias<Bin>(b => b.Size, "volume");

        Should.Throw<InvalidOperationException>(() => ODataModel.Build(profile, app.App.Services.GetRequiredService<ODataEntityRegistry>(),
            app.App.Services.GetRequiredService<Computed.ODataComputedProperties>(), null, findShared: false, null)).Message.ShouldContain("volume");
    }

    private async Task<List<FacetGroupDto>> FacetsAsync(HttpClient client, string url, FacetContextDto? context = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");
        if (context is not null)
        {
            request.Headers.Add(FacetContextDto.Header, context.Encode());
        }

        using var response = await client.SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("@cn.facets").Deserialize<List<FacetGroupDto>>(JsonSerializerOptions.Web)!;
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
