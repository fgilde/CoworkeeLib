using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

/// <summary>
/// The checks of an OData query read the options as OData parses them, and every way a query reaches other rows goes through the navigation
/// scope: spelled keys, aliases, [AutoExpand], facets and their labels, computed properties behind navigations, two filtered levels, search titles.
/// </summary>
public sealed class QueryCheckTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _plain;
    private Guid _secret;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        (_plain, _secret) = await app.InDbAsync(_setup.TenantId, async db =>
        {
            var tenant = _setup.TenantId;
            var gold = new Badge { Name = "Gold", TenantId = tenant };
            var plain = new Bin
            {
                Name = "Plain", Size = 1, Badge = gold, TenantId = tenant, Labels = [new BinLabel { Text = "Fragile", TenantId = tenant }],
            };
            var secret = new Bin { Name = "Secret", Size = 9, TenantId = tenant, Labels = [new BinLabel { Text = "Fragile", TenantId = tenant }] };
            var visible = new Bin { Name = "Visible", Size = 2, Badge = new Badge { Name = "Secret", TenantId = tenant }, TenantId = tenant };
            var open = new Shelf { Name = "Open", TenantId = tenant, Bins = [plain, secret], Pegs = [new Peg { Name = "P1", TenantId = tenant }] };
            var other = new Shelf { Name = "Other", TenantId = tenant };
            var third = new Shelf { Name = "Third", TenantId = tenant, Bins = [visible] };
            db.AddRange(open, other, third, new Badge { Name = "Silver", TenantId = tenant });
            await db.SaveChangesAsync();
            (open.Featured, other.Featured, third.Featured) = (plain, secret, visible);
            var locker = new Locker { Name = "L1", ShelfId = open.Id, TenantId = tenant };
            db.AddRange(new Tray { Name = "T1", Bin = plain, Shelf = open, Locker = locker, TenantId = tenant }, new Tray { Name = "T2", Bin = secret, Shelf = open, TenantId = tenant });
            await db.SaveChangesAsync();
            return (plain.Id, secret.Id);
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private async Task<JsonElement> GetAsync(string url)
    {
        using var response = await Admin.GetAsync(url, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static string?[] Names(JsonElement rows, string property = "Name") =>
        [.. rows.EnumerateArray().Select(r => r.ValueKind == JsonValueKind.Null ? null : r.GetProperty(property).GetString())];

    [Fact]
    public async Task Option_keys_with_spaces_and_aliases_do_not_pass_the_checks()
    {
        var viewer = app.As(await UserWithAsync(ODataApp.ViewGadgets), _setup.TenantId);
        foreach (var query in new[] { "+$expand=Bins", "%20$expand=Bins", "$expand=Bins+" })
        {
            (await viewer.GetAsync($"/odata/Shelves?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, query);
        }

        (await GetAsync("/odata/Shelves?+$filter=Bins/any(b: b/Name eq 'Secret')&$count=true")).GetProperty("@odata.count").GetInt32().ShouldBe(0);

        var binViewer = app.As(await UserWithAsync(ODataApp.ViewGadgets, ODataApp.ViewBins), _setup.TenantId);
        foreach (var query in new[] { "+$filter=Size gt 5", "+$orderby=Size", "+$select=Volume", "%20$select=Name,Volume" })
        {
            (await binViewer.GetAsync($"/odata/Bins?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, query);
        }

        foreach (var query in new[] { "$filter=@a gt 5&@a=Size", "$orderby=@a&+@a=Size" })
        {
            var response = await binViewer.GetAsync($"/odata/Bins?{query}", Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
            (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("aliases");
        }

        (await Admin.GetAsync("/api/v1/data/Bins/export?format=csv&$filter=@a gt 5&@a=Size", Ct)).IsSuccessStatusCode.ShouldBeFalse();
        (await binViewer.GetAsync("/api/v1/data/Bins/export?format=csv&+$filter=Size gt 5", Ct)).IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task The_checks_read_the_bound_query_whatever_its_spelling()
    {
        var viewer = await UserWithAsync(ODataApp.ViewGadgets, ODataApp.ViewBins);
        var binViewer = app.As(viewer, _setup.TenantId);
        foreach (var query in new[]
                 {
                     "$compute=Featured/ Size as S&$select=Name,S", "$filter=Featured/ Volume gt 0", "$orderby=Featured/ Size", "$filter=Bins/any(b: b/ Size gt 1)",
                     "$expand=Bins($filter=Size gt 1)", "$expand=Featured($compute=Size as S;$select=Name,S)", "$expand=Featured($expand=Badge;$orderby=Size)",
                 })
        {
            (await binViewer.GetAsync($"/odata/Shelves?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, query);
        }

        // names inside literals are no properties
        foreach (var query in new[] { "$filter=Name eq 'Featured/Size'", "$expand=Bins($filter=Name eq 'Size')", "$expand=Featured($expand=Badge($filter=Name ne 'Volume'))" })
        {
            (await binViewer.GetAsync($"/odata/Shelves?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, query);
        }

        // the other paths that take a filter: export, facet filters, the data tools and reports (FilteredAsync)
        (await binViewer.GetAsync("/api/v1/data/Shelves/export?format=csv&$filter=Featured/ Size gt 0", Ct)).IsSuccessStatusCode.ShouldBeFalse();
        using var facets = new HttpRequestMessage(HttpMethod.Get, "/odata/Shelves");
        facets.Headers.Add(FacetContextDto.Header, new FacetContextDto("Featured/ Size gt 0", new Dictionary<string, string>()).Encode());
        (await binViewer.SendAsync(facets, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var actor = Coworkee.Core.Security.CurrentUserScope.Begin(new Coworkee.Core.Security.ImpersonatedUser(viewer, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        await Should.ThrowAsync<Microsoft.OData.ODataException>(() => ODataEntityAccess.FilteredAsync<Shelf>(scope.ServiceProvider, "Featured/ Size gt 0", Ct));
        (await ODataEntityAccess.FilteredAsync<Shelf>(scope.ServiceProvider, "Name eq 'Featured/Size'", Ct)).ShouldNotBeNull();
        var dispatcher = scope.ServiceProvider.GetRequiredService<Coworkee.Application.Messaging.IDispatcher>();
        (await dispatcher.SendAsync(new Coworkee.OData.Transfer.QueryDataQuery("Shelves", "Featured/ Size gt 0"), Ct)).Error!.Kind.ShouldBe(Coworkee.Core.Results.ErrorKind.Validation);
    }

    [Fact]
    public async Task What_the_checks_cannot_parse_is_not_run()
    {
        using var facets = new HttpRequestMessage(HttpMethod.Get, "/odata/Shelves");
        facets.Headers.Add(FacetContextDto.Header, new FacetContextDto("Nope eq 1", new Dictionary<string, string>()).Encode());
        (await Admin.SendAsync(facets, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.GetAsync("/odata/Shelves?$filter=Featured/Size gt 0&$orderby=Nope", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.GetAsync("/api/v1/data/Shelves/export?format=csv&$filter=Nope eq 1", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void A_node_the_checks_cannot_read_fails_them()
    {
        Should.Throw<Microsoft.OData.ODataException>(() => ODataQueryGuard.Read(new Unreadable()));
    }

    [Fact]
    public async Task Facets_through_a_reference_by_key_need_a_readable_mapped_foreign_key()
    {
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            var tenant = _setup.TenantId;
            var bin = await db.Set<Bin>().FirstAsync(b => b.Name == "Plain", Ct);
            var hook = new Hook { Name = "H1", Main = bin, Brand = new Brand { Code = "AC", Name = "Acme", TenantId = tenant }, TenantId = tenant };
            db.Add(hook);
            db.Entry(hook).Property("SpareId").CurrentValue = bin.Id;
            return await db.SaveChangesAsync(Ct);
        });

        // through a filtered target every kind of key stays and counts the visible targets: a mapped foreign key (Main), a shadow one (Spare),
        // a key that is not EF's (Brand/Code)
        var admin = (await FacetsAsync(Admin, "$top=0", "Hooks")).Facets;
        foreach (var field in new[] { "Main", "Spare", "Brand" })
        {
            admin.Single(f => f.Field == field).Options.Single().Count.ShouldBe(1, field);
        }

        // the foreign key is protected: the facet reads the target's key, which the user may read there, as $expand=Main($select=Id) shows it
        var viewer = app.As(await UserWithAsync(ODataApp.ViewGadgets, ODataApp.ViewBins), _setup.TenantId);
        (await FacetsAsync(viewer, "$top=0", "Hooks")).Facets.Single(f => f.Field == "Main").Options.Single().Value.GetString().ShouldBe(_plain.ToString(), StringCompareShould.IgnoreCase);

        // a collection cannot be grouped by: refused when the model is built
        Should.Throw<InvalidOperationException>(() => FacetResponse.RefuseCollectionPaths(typeof(Rack))).Message.ShouldContain("collection");
        FacetResponse.RefuseCollectionPaths(typeof(Hook));
        FacetResponse.RefuseCollectionPaths(typeof(Tray));
    }

    [Fact]
    public async Task Search_fields_through_a_navigation_without_rules_stay()
    {
        (await GetAsync("/odata/Trays?$search=open")).GetProperty("value").GetArrayLength().ShouldBe(2);
        (await GetAsync("/odata/Trays?$search=plain")).GetProperty("value").GetArrayLength().ShouldBe(0);
    }

    private sealed class Unreadable
    {
        public string Value => throw new InvalidOperationException("no");
    }

    [Fact]
    public async Task Apply_through_a_navigation_without_rules_still_groups()
    {
        var groups = (await GetAsync("/odata/Trays?$apply=groupby((Shelf/Name),aggregate($count as N))")).GetProperty("value");
        groups.GetArrayLength().ShouldBe(1);
        (await Admin.GetAsync("/odata/Trays?$apply=groupby((Bin/Name),aggregate($count as N))", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Auto_expanded_navigations_follow_their_target()
    {
        var trays = (await GetAsync("/odata/Trays?$orderby=Name")).GetProperty("value");
        trays[0].GetProperty("Bin").GetProperty("Name").GetString().ShouldBe("Plain");
        trays[1].GetProperty("Bin").ValueKind.ShouldBe(JsonValueKind.Null);

        // a set the user may not read is not expanded behind his back, and the list stays readable
        var viewer = app.As(await UserWithAsync(ODataApp.ViewGadgets), _setup.TenantId);
        var page = await viewer.GetFromJsonAsync<JsonElement>("/odata/Trays", Ct);
        page.GetProperty("value").EnumerateArray().ShouldAllBe(t => t.GetProperty("Bin").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Facets_count_and_label_only_rows_the_user_may_see()
    {
        // the open shelf has a hidden bin named Secret: without the scope its facet would count 1
        var (_, facets) = await FacetsAsync(Admin, "$filter=Bins/any(b: b/Name eq 'Secret')&$top=0");
        facets.SelectMany(f => f.Options).ShouldAllBe(o => o.Count == 0);

        // a value read through a navigation into filtered rows counts only the visible targets; a target without rules counts all
        var trays = (await FacetsAsync(Admin, "$top=0", "Trays")).Facets;
        trays.Single(f => f.Field == "Shelf").Options.Single().Label.ShouldBe("Open");
        trays.Single(f => f.Field == "Bin").Options.Single().Value.GetString().ShouldBe(_plain.ToString(), StringCompareShould.IgnoreCase);

        var featured = (await FacetsAsync(Admin, "$top=0")).Facets.Single(f => f.Field == "FeaturedId").Options;
        featured.Select(o => o.Label.ToLowerInvariant()).ShouldBe(["plain", "visible", _secret.ToString()], ignoreOrder: true);

        // bins are not readable at all: no label names one
        var viewer = app.As(await UserWithAsync(ODataApp.ViewGadgets), _setup.TenantId);
        var hidden = (await FacetsAsync(viewer, "$top=0")).Facets.Single(f => f.Field == "FeaturedId").Options;
        hidden.Count.ShouldBe(3);
        hidden.Select(o => Guid.TryParse(o.Label, out var id)).ShouldAllBe(parsed => parsed);
        hidden.Single(o => o.Label.Equals(_plain.ToString(), StringComparison.OrdinalIgnoreCase)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Facets_through_a_filtered_target_count_only_its_visible_rows()
    {
        // the other shelf's featured bin is Secret, hidden by the bins' row filter: that shelf has no value, as $expand=Featured shows null
        var featured = (await FacetsAsync(Admin, "$top=0")).Facets.Single(f => f.Field == "Featured").Options;
        featured.Select(o => (o.Value.GetString(), o.Label, o.Count)).ShouldBe([("Plain", "Plain", 1), ("Visible", "Visible", 1)], ignoreOrder: true);
        (await GetAsync("/odata/Shelves?$filter=Name eq 'Other'&$expand=Featured")).GetProperty("value")[0].GetProperty("Featured").ValueKind.ShouldBe(JsonValueKind.Null);

        // nothing names the hidden target, its key included
        var trays = JsonSerializer.Serialize((await FacetsAsync(Admin, "$top=0", "Trays")).Facets.Where(f => f.Field == "Bin"));
        var shelves = JsonSerializer.Serialize((await FacetsAsync(Admin, "$top=0")).Facets.Where(f => f.Field == "Featured"));
        foreach (var json in new[] { trays, shelves })
        {
            json.ShouldNotContain("Secret");
            json.ShouldNotContain(_secret.ToString(), Case.Insensitive);
        }

        // an "any of" choice in the group keeps its alternatives; the other groups count with it
        using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Shelves?$filter=Featured/Name eq 'Plain'&$top=0");
        request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");
        request.Headers.Add(FacetContextDto.Header, new FacetContextDto(null, new Dictionary<string, string> { ["featured"] = "Featured/Name eq 'Plain'" }).Encode());
        using var response = await Admin.SendAsync(request, Ct);
        var facets = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("@cn.facets").Deserialize<List<FacetGroupDto>>(JsonSerializerOptions.Web)!;
        facets.Single(f => f.Field == "Featured").Options.Select(o => (o.Label, o.Count)).ShouldBe([("Plain", 1), ("Visible", 1)], ignoreOrder: true);
        facets.Single(f => f.Field == "Name").Options.Where(o => o.Count > 0).Select(o => o.Label).ShouldBe(["Open"]);

        // through an owned type, as an order's buyer, supplier and ship to: the value through the filtered bin counts the visible ones, the
        // owned foreign key is the row's own value and its label shows the key of a hidden bin
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(
                new Hook { Name = "H2", Front = new Mount { BinId = _plain }, Back = new Mount { BinId = _plain }, TenantId = _setup.TenantId },
                new Hook { Name = "H3", Front = new Mount { BinId = _secret }, Back = new Mount { BinId = _secret }, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync(Ct);
        });
        var hooks = (await FacetsAsync(Admin, "$top=0", "Hooks")).Facets;
        hooks.Single(f => f.Field == "Back").Options.Select(o => (o.Label, o.Count)).ShouldBe([("Plain", 1)]);
        hooks.Single(f => f.Field == "Front").Options.Select(o => o.Label.ToLowerInvariant()).ShouldBe(["plain", _secret.ToString()], ignoreOrder: true);

        // a target the user may not read and one whose filter is no Where leave the group out, also through an owned type
        var viewer = app.As(await UserWithAsync(ODataApp.ViewGadgets), _setup.TenantId);
        (await FacetsAsync(viewer, "$top=0")).Facets.ShouldNotContain(f => f.Field == "Featured");
        (await FacetsAsync(viewer, "$top=0", "Hooks")).Facets.ShouldNotContain(f => f.Field == "Back");
        // a value type read through that target is null in a filter too (EF folded the constant test and failed)
        (await viewer.GetFromJsonAsync<JsonElement>("/odata/Hooks?$filter=Main/Id ne null", Ct)).GetProperty("value").GetArrayLength().ShouldBe(0);
        (await FacetsAsync(Admin, "$top=0", "Trays")).Facets.ShouldNotContain(f => f.Field == "Locker");
    }

    [Fact]
    public async Task A_range_through_a_filtered_target_spans_only_the_visible_targets()
    {
        // the featured bins are Plain (1), Secret (9, hidden) and Visible (2)
        var depth = (await FacetsAsync(Admin, "$top=0")).Facets.Single(f => f.Field == "Depth");
        (depth.Min, depth.Max).ShouldBe(("1", "2"));
        depth.Options.Sum(o => o.Count).ShouldBe(2);
    }

    [Fact]
    public async Task Facets_need_every_member_their_value_and_label_read()
    {
        var viewer = app.As(await UserWithAsync(ODataApp.ViewGadgets, ODataApp.ViewBins), _setup.TenantId);

        // the label reads Bin/Size, which needs SeeCategories
        (await FacetsAsync(Admin, "$top=0", "Trays")).Facets.ShouldContain(f => f.Field == "BinId");
        (await FacetsAsync(viewer, "$top=0", "Trays")).Facets.ShouldNotContain(f => f.Field == "BinId");

        // the facet sits on Name but its value is MainId, which needs SeeCategories
        (await FacetsAsync(Admin, "$top=0", "Hooks")).Facets.ShouldContain(f => f.Field == "Name");
        (await FacetsAsync(viewer, "$top=0", "Hooks")).Facets.ShouldNotContain(f => f.Field == "Name");
    }

    [Fact]
    public async Task Facet_groups_the_scope_did_not_check_are_left_out_without_failing()
    {
        var paths = FacetResponse.Paths(typeof(Shelf));
        FacetResponse.Checked(new Nextended.Core.Facets.FacetGroup { Key = "featured", Field = "Featured", ValuePath = "it/Featured/Name" }, paths).ShouldBeTrue();
        FacetResponse.Checked(new Nextended.Core.Facets.FacetGroup { Key = "x", Field = "Name", ValuePath = "Featured/Badge/Name" }, paths).ShouldBeFalse();
        FacetResponse.Checked(new Nextended.Core.Facets.FacetGroup { Key = "y", Field = "FeaturedId", ValuePath = "FeaturedId", LabelPath = "Featured/Badge/Name" }, paths).ShouldBeFalse();

        // the builder's query through a target nobody checked shows no rows there instead of failing the request
        await using var services = app.App.Services.CreateAsyncScope();
        var db = services.ServiceProvider.GetRequiredService<GadgetDbContext>();
        var scope = ODataNavigationScope.For(services.ServiceProvider, typeof(Shelf));
        var shelves = db.Set<Shelf>().IgnoreQueryFilters().Where(s => s.TenantId == _setup.TenantId);
        var names = await ScopedQueryProvider.Wrap(shelves, scope).Select(s => s.Featured!.Name).ToListAsync(Ct);
        names.Count.ShouldBe(3);
        names.ShouldAllBe(n => n == null);
        Should.Throw<InvalidOperationException>(() => scope.Apply(shelves.Select(s => s.Featured!.Name)));
    }

    [Fact]
    public async Task A_row_filter_that_fails_behind_navigations_is_logged_once_per_type()
    {
        await GetAsync("/odata/Shelves?$filter=Pegs/any()");
        await GetAsync("/odata/Shelves?$filter=Pegs/any()");

        app.Logs.Warnings.Count(w => w.Contains("row filters of Peg", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task Computed_properties_behind_a_navigation_need_their_permission()
    {
        var viewer = app.As(await UserWithAsync(ODataApp.ViewGadgets, ODataApp.ViewBins), _setup.TenantId);
        foreach (var query in new[] { "$expand=Bins($select=Volume)", "$expand=Featured($select=Name,Volume)", "$filter=Featured/Volume gt 0", "$filter=Bins/any(b: b/Volume gt 0)", "$orderby=Featured/Volume" })
        {
            (await viewer.GetAsync($"/odata/Shelves?{query}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, query);
        }

        var shelf = (await GetAsync("/odata/Shelves?$filter=Name eq 'Open'&$expand=Featured($select=Name,Volume)")).GetProperty("value")[0];
        shelf.GetProperty("Featured").GetProperty("Volume").GetInt32().ShouldBe(10);
    }

    [Fact]
    public async Task Two_filtered_reference_levels_and_a_collection_under_a_hidden_reference()
    {
        var shelves = (await GetAsync("/odata/Shelves?$expand=Featured($expand=Badge)&$orderby=Name")).GetProperty("value");
        Names(shelves).ShouldBe(["Open", "Other", "Third"]);
        shelves[0].GetProperty("Featured").GetProperty("Badge").GetProperty("Name").GetString().ShouldBe("Gold");
        shelves[1].GetProperty("Featured").ValueKind.ShouldBe(JsonValueKind.Null);
        shelves[2].GetProperty("Featured").GetProperty("Badge").ValueKind.ShouldBe(JsonValueKind.Null);

        Names((await GetAsync("/odata/Shelves?$filter=Featured/Badge/Name eq 'Gold'")).GetProperty("value")).ShouldBe(["Open"]);
        (await GetAsync("/odata/Shelves?$filter=Featured/Badge/Name eq 'Secret'")).GetProperty("value").GetArrayLength().ShouldBe(0);
        Names((await GetAsync("/odata/Shelves?$filter=Featured/Badge/Name ne null&$orderby=Featured/Badge/Name desc,Name")).GetProperty("value")).ShouldBe(["Open"]);
        (await GetAsync("/odata/Shelves?$orderby=Featured/Badge/Name,Name")).GetProperty("value").GetArrayLength().ShouldBe(3);

        // the secret bin carries a Fragile label too; behind the hidden reference it is not there
        Names((await GetAsync("/odata/Shelves?$filter=Featured/Labels/any(l: l/Text eq 'Fragile')")).GetProperty("value")).ShouldBe(["Open"]);
        var labels = (await GetAsync("/odata/Shelves?$expand=Featured($select=Name,Labels)&$orderby=Name")).GetProperty("value");
        labels[0].GetProperty("Featured").GetProperty("Labels").GetArrayLength().ShouldBe(1);
        labels[1].GetProperty("Featured").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_filter_that_fails_on_the_scope_hides_only_its_own_type()
    {
        (await GetAsync("/odata/Shelves?$expand=Bins&$filter=Name eq 'Open'")).GetProperty("value")[0].GetProperty("Bins").GetArrayLength().ShouldBe(1);
        (await GetAsync("/odata/Pegs")).GetProperty("value").GetArrayLength().ShouldBe(1);
        (await Admin.GetAsync("/odata/Shelves?$expand=Pegs", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetAsync("/odata/Shelves?$filter=Pegs/any()")).GetProperty("value").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_count_segment_counts_only_visible_rows()
    {
        (await CountAsync("$filter=Bins/any(b: b/Name eq 'Secret')")).ShouldBe("0");
        (await CountAsync("$filter=Bins/any()")).ShouldBe("2");
    }

    [Fact]
    public async Task Apply_and_compute_through_a_navigation_see_only_visible_rows()
    {
        // a group of a hidden target would carry empty values the model does not allow: refused
        foreach (var apply in new[] { "groupby((Featured/Name),aggregate($count as N))", "filter(Bins/any(b: b/Name eq 'Secret'))", "aggregate(Bins($count as N))" })
        {
            (await Admin.GetAsync($"/odata/Shelves?$apply={apply}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, apply);
        }

        (await GetAsync("/odata/Shelves?$apply=groupby((Name),aggregate($count as N))")).GetProperty("value").GetArrayLength().ShouldBe(3);

        var computed = (await GetAsync("/odata/Shelves?$compute=Featured/Name as FeaturedName&$select=Name,FeaturedName&$orderby=Name")).GetProperty("value");
        computed.EnumerateArray().Select(s => s.GetProperty("FeaturedName").ValueKind == JsonValueKind.Null ? null : s.GetProperty("FeaturedName").GetString())
            .ShouldBe(["Plain", null, "Visible"]);
    }

    [Fact]
    public async Task The_page_size_of_the_model_stays()
    {
        var page = await GetAsync("/odata/Badges?$orderby=Name");

        Names(page.GetProperty("value")).ShouldBe(["Gold"]);
        page.TryGetProperty("@odata.nextLink", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Search_titles_through_a_navigation_show_only_visible_rows()
    {
        var hits = (await Admin.GetFromJsonAsync<GlobalSearchResult>("/api/v1/search?types=trays&q=t", Ct))!.Hits;
        hits.Select(h => h.Title).ShouldNotContain("Secret");

        (await Admin.GetFromJsonAsync<GlobalSearchResult>("/api/v1/search?types=trays&q=plain", Ct))!.Hits.Single().Title.ShouldBe("Plain");
        (await Admin.GetFromJsonAsync<GlobalSearchResult>("/api/v1/search?types=trays&q=secret", Ct))!.Hits.ShouldBeEmpty();
    }

    private async Task<string> CountAsync(string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/odata/Shelves/$count?{query}");
        request.Headers.Accept.ParseAdd("text/plain");
        using var response = await Admin.SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<(string[] Names, List<FacetGroupDto> Facets)> FacetsAsync(HttpClient client, string query, string set = "Shelves")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/odata/{set}?{query}");
        request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");
        using var response = await client.SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var page = JsonDocument.Parse(body).RootElement;
        return ([.. page.GetProperty("value").EnumerateArray().Select(r => r.GetProperty("Name").GetString()!)],
            page.GetProperty("@cn.facets").Deserialize<List<FacetGroupDto>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
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
