using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Query;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData.DataFilters;
using Coworkee.Contracts.Search;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

// layered data filters applied: every read of visible rows (lists, counts, facets, navigations, key reads, links, search, reports, exports, client copies)
public sealed class DataFilterApplyTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Lists_counts_and_facets_see_only_filtered_rows()
    {
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            var bolt = Widget("Bolt", 5, "Blue");
            db.AddRange(Widget("Anvil", 50, "Red"), bolt, Widget("Clamp", 5, "Red"), Widget("Drill", 20, "Green"),
                new Part { Name = "of-bolt", Widget = bolt, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync(Ct);
        });
        var user = Client(await UserWithAsync(ODataApp.ViewGadgets));
        await FilterAsync("Widgets", DataFilterLayer.Tenant, null, Rule("Price", QueryOperator.GreaterThan, "10"));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Widgets?$orderby=Name&$count=true&$top=1");
        request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");
        var response = await user.SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(body);
        var page = JsonDocument.Parse(body).RootElement;

        Names(page.GetProperty("value")).ShouldBe(["Anvil"]);
        page.GetProperty("@odata.count").GetInt32().ShouldBe(2);
        var color = page.GetProperty("@cn.facets").Deserialize<List<FacetGroupDto>>(Web)!.Single(f => f.Key == "color");
        color.Options.Select(o => (o.Value.GetString(), o.Count)).ShouldBe([("Green", 1), ("Red", 1)], ignoreOrder: true);
        (await user.GetStringAsync("/odata/Widgets/$count", Ct)).ShouldBe("2");
        // the user's own filter sits on top
        Names((await user.GetFromJsonAsync<JsonElement>("/odata/Widgets?$filter=Color eq 'Red'", Ct)).GetProperty("value")).ShouldBe(["Anvil"]);
        // Bypass: no layer at all
        (await Client(await UserWithAsync(ODataApp.ViewGadgets, DataFilterPermissions.Bypass)).GetStringAsync("/odata/Widgets/$count", Ct)).ShouldBe("4");
    }

    [Fact]
    public async Task Expand_and_navigation_filters_do_not_reveal_filtered_rows()
    {
        var (open, other) = await ShelvesAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            var (anvil, bolt) = (Widget("Anvil", 50, "Red"), Widget("Bolt", 5, "Blue"));
            db.AddRange(new Part { Name = "of-anvil", Widget = anvil, TenantId = _setup.TenantId }, new Part { Name = "of-bolt", Widget = bolt, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync(Ct);
        });
        var user = Client(await UserWithAsync(ODataApp.ViewGadgets, ODataApp.ViewBins));
        await FilterAsync("Bins", DataFilterLayer.Tenant, null, Rule("Size", QueryOperator.GreaterThan, "2"));
        await FilterAsync("Widgets", DataFilterLayer.Tenant, null, Rule("Price", QueryOperator.GreaterThan, "10"));

        Names((await GetAsync(user, "/odata/Bins")).GetProperty("value")).ShouldBe(["Big", "Gone"]);
        var shelf = await GetAsync(user, $"/odata/Shelves({open})?$expand=Bins($count=true),Featured");
        Names(shelf.GetProperty("Bins")).ShouldBe(["Big"]);
        shelf.GetProperty("Bins@odata.count").GetInt32().ShouldBe(1);
        shelf.GetProperty("Featured").ValueKind.ShouldBe(JsonValueKind.Null);
        (await GetAsync(user, $"/odata/Shelves({other})?$expand=Featured")).GetProperty("Featured").GetProperty("Name").GetString().ShouldBe("Big");
        (await GetAsync(user, "/odata/Shelves?$filter=Bins/any(b: b/Name eq 'Small')&$count=true")).GetProperty("@odata.count").GetInt32().ShouldBe(0);
        (await GetAsync(user, "/odata/Shelves?$filter=Featured/Name eq 'Small'")).GetProperty("value").GetArrayLength().ShouldBe(0);
        (await GetAsync(user, "/odata/Shelves?$filter=Bins/$count eq 2")).GetProperty("value").GetArrayLength().ShouldBe(0);
        // the bin reached through a tray, expanded without $expand
        (await GetAsync(user, "/odata/Trays?$filter=Name eq 't-small'")).GetProperty("value")[0].GetProperty("Bin").ValueKind.ShouldBe(JsonValueKind.Null);
        // child data of a filtered-out parent disappears with it
        Names((await GetAsync(user, "/odata/Parts")).GetProperty("value")).ShouldBe(["of-anvil"]);
    }

    [Fact]
    public async Task Key_reads_follow_the_details_switch()
    {
        var (open, _) = await ShelvesAsync();
        var (hammer, hat, rag) = await app.InDbAsync(_setup.TenantId, async db =>
        {
            var (a, h, r) = (Gadget("Hammer", "Tools", 5), Gadget("Hat", "Junk", 5), Gadget("Rag", "Junk", 1));
            db.AddRange(a, h, r);
            await db.SaveChangesAsync(Ct);
            return (a.Id, h.Id, r.Id);
        });
        var id = await UserWithAsync(ODataApp.ViewGadgets, ODataApp.ViewBins);
        var user = Client(id);
        await FilterAsync("Gadgets", DataFilterLayer.System, null, Rule("Price", QueryOperator.GreaterThan, "1"));
        await FilterAsync("Gadgets", DataFilterLayer.Tenant, null, Rule("Category", QueryOperator.Equal, "Tools"), appliesToDetails: false);
        await FilterAsync("Bins", DataFilterLayer.Tenant, null, Rule("Size", QueryOperator.GreaterThan, "2"), appliesToDetails: false);

        Names((await GetAsync(user, "/odata/Gadgets")).GetProperty("value")).ShouldBe(["Hammer"]);
        // the list hides it, a key read opens it; the filter that applies to details too still hides
        (await GetAsync(user, $"/odata/Gadgets({hat})")).GetProperty("Name").GetString().ShouldBe("Hat");
        (await user.GetAsync($"/odata/Gadgets({rag})", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // opening a record by id (attachments, social, history) follows the key read
        (await VisibleIdsAsync<Gadget>(id)).ShouldBe([hammer, hat], ignoreOrder: true);
        // what a key read expands is a list: its rows stay filtered
        Names((await GetAsync(user, $"/odata/Shelves({open})?$expand=Bins")).GetProperty("Bins")).ShouldBe(["Big"]);
    }

    [Fact]
    public async Task A_linked_set_follows_its_target_by_key()
    {
        await ShelvesAsync();
        var trays = await app.InDbAsync(_setup.TenantId, db => db.Set<Tray>().ToDictionaryAsync(t => t.Name, t => t.Id, Ct));
        var user = Client(await UserWithAsync(ODataApp.ViewGadgets, ODataApp.ViewBins));
        await LinkAsync("Trays", nameof(Tray.BinId), "Bins");

        // a target without filters: the link adds nothing
        Names((await GetAsync(user, "/odata/Trays")).GetProperty("value")).ShouldBe(["t-big", "t-gone", "t-none", "t-small"]);

        await FilterAsync("Bins", DataFilterLayer.Tenant, null, Rule("Size", QueryOperator.GreaterThan, "2"), appliesToDetails: false);
        await FilterAsync("Bins", DataFilterLayer.System, null, Rule("Name", QueryOperator.NotEqual, "Gone"));

        // a tray is visible only when its bin passes the bins' filters (no bin: none to pass)
        Names((await GetAsync(user, "/odata/Trays")).GetProperty("value")).ShouldBe(["t-big"]);
        (await GetAsync(user, "/odata/Trays/$count")).GetInt32().ShouldBe(1);
        // by key the target's details switch counts: the bin opens by key, so does its tray
        (await GetAsync(user, $"/odata/Trays({trays["t-small"]})")).GetProperty("Name").GetString().ShouldBe("t-small");
        (await user.GetAsync($"/odata/Trays({trays["t-gone"]})", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await user.GetAsync($"/odata/Trays({trays["t-none"]})", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // a chain of links that comes back to itself is broken: it hides every row
        await LinkAsync("Bins", nameof(Bin.ShelfId), "Shelves");
        await LinkAsync("Shelves", nameof(Shelf.FeaturedId), "Bins");
        (await GetAsync(user, "/odata/Trays/$count")).GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task A_linked_set_behind_a_navigation_follows_its_target()
    {
        var (open, other) = await ShelvesAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            var (gold, tin) = (new Badge { Name = "Gold", TenantId = _setup.TenantId }, new Badge { Name = "Tin", TenantId = _setup.TenantId });
            db.AddRange(gold, tin);
            await db.SaveChangesAsync(Ct);
            var bins = await db.Set<Bin>().ToDictionaryAsync(b => b.Name, Ct);
            (bins["Small"].BadgeId, bins["Big"].BadgeId) = (gold.Id, tin.Id);
            return await db.SaveChangesAsync(Ct);
        });
        var user = Client(await UserWithAsync(ODataApp.ViewGadgets, ODataApp.ViewBins));
        await LinkAsync("Bins", nameof(Bin.BadgeId), "Badges");
        await FilterAsync("Badges", DataFilterLayer.Tenant, null, Rule("Name", QueryOperator.Equal, "Gold"));

        // the bins whose badge passes the badges' filters, in their own set and behind every navigation into them
        Names((await GetAsync(user, "/odata/Bins")).GetProperty("value")).ShouldBe(["Small"]);
        Names((await GetAsync(user, $"/odata/Shelves({open})?$expand=Bins")).GetProperty("Bins")).ShouldBe(["Small"]);
        (await GetAsync(user, $"/odata/Shelves({other})?$expand=Featured")).GetProperty("Featured").ValueKind.ShouldBe(JsonValueKind.Null);
        (await GetAsync(user, "/odata/Shelves?$filter=Bins/any(b: b/Name eq 'Big')&$count=true")).GetProperty("@odata.count").GetInt32().ShouldBe(0);
        (await GetAsync(user, "/odata/Shelves?$filter=Bins/any(b: b/Name eq 'Small')&$count=true")).GetProperty("@odata.count").GetInt32().ShouldBe(1);
        (await GetAsync(user, "/odata/Trays?$filter=Name eq 't-big'")).GetProperty("value")[0].GetProperty("Bin").ValueKind.ShouldBe(JsonValueKind.Null);
        (await GetAsync(user, "/odata/Trays?$filter=Name eq 't-small'")).GetProperty("value")[0].GetProperty("Bin").GetProperty("Name").GetString().ShouldBe("Small");
    }

    [Fact]
    public async Task Search_reports_exports_and_client_snapshots_see_the_same_rows()
    {
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(Gadget("Lamp", "Light", 35), Gadget("Lamp shade", "Light", 1), Gadget("Clamp", "Tools", 12), Gadget("Drill", "Tools", 99));
            return await db.SaveChangesAsync(Ct);
        });
        var id = await UserWithAsync(ODataApp.ViewGadgets);
        var user = Client(id);
        await FilterAsync("Gadgets", DataFilterLayer.Tenant, null, Rule("Price", QueryOperator.GreaterThan, "10"));
        await FilterAsync("Gadgets", DataFilterLayer.System, null, Rule("Name", QueryOperator.Contains, "amp"));
        string[] expected = ["Clamp", "Lamp"];

        Names((await GetAsync(user, "/odata/Gadgets")).GetProperty("value")).ShouldBe(expected);
        // global search
        var search = await user.GetFromJsonAsync<GlobalSearchResult>("/api/v1/search?types=gadgets&q=amp", Ct);
        search!.Hits.Select(h => h.Title).Order().ShouldBe(expected);
        // export
        var csv = await user.GetStringAsync("/api/v1/data/Gadgets/export?format=csv&columns=Name&$orderby=Name", Ct);
        csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).ShouldBe(expected);
        // reports (Dynamic Reports read through FilteredAsync), with and without a user filter
        (await FilteredAsync(id, null)).ShouldBe(expected);
        (await FilteredAsync(id, "Price gt 20")).ShouldBe(["Lamp"]);
        // the client copy: snapshot and the ids of the feed
        (await SnapshotAsync(user)).ShouldBe(expected);
        (await user.GetFromJsonAsync<Guid[]>("/api/v1/client-entities/Gadgets/ids", Ct))!.Length.ShouldBe(2);
    }

    // shelves "Open" (bins Small 1, Big 5; featured Small) and "Other" (featured Big); trays on Small, Big, Gone (size 9) and none
    private Task<(Guid Open, Guid Other)> ShelvesAsync() => app.InDbAsync(_setup.TenantId, async db =>
    {
        var tenant = _setup.TenantId;
        var (small, big, gone) = (new Bin { Name = "Small", Size = 1, TenantId = tenant }, new Bin { Name = "Big", Size = 5, TenantId = tenant }, new Bin { Name = "Gone", Size = 9, TenantId = tenant });
        var open = new Shelf { Name = "Open", TenantId = tenant, Bins = [small, big] };
        var other = new Shelf { Name = "Other", TenantId = tenant, Bins = [gone] };
        db.AddRange(open, other);
        await db.SaveChangesAsync(Ct);
        (open.Featured, other.Featured) = (small, big);
        db.AddRange(
            new Tray { Name = "t-small", BinId = small.Id, TenantId = tenant }, new Tray { Name = "t-big", BinId = big.Id, TenantId = tenant },
            new Tray { Name = "t-gone", BinId = gone.Id, TenantId = tenant }, new Tray { Name = "t-none", TenantId = tenant });
        await db.SaveChangesAsync(Ct);
        return (open.Id, other.Id);
    });

    private Widget Widget(string name, decimal price, string color) =>
        new() { Name = name, Price = price, Color = color, MadeAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), TenantId = _setup.TenantId };

    private Gadget Gadget(string name, string category, decimal price) => new() { Name = name, Category = category, Price = price, TenantId = _setup.TenantId };

    private static string Rule(string field, QueryOperator op, string value) =>
        QueryJson.Serialize(new QueryGroup { Children = [new QueryRule { Field = field, Operator = op, Value = value }] });

    private static string[] Names(JsonElement rows) => [.. rows.EnumerateArray().Select(r => r.GetProperty("Name").GetString()!).Order(StringComparer.Ordinal)];

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"{url}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task FilterAsync(string set, DataFilterLayer layer, Guid? key, string query, bool appliesToDetails = true)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        db.Add(new DataFilter
        {
            TenantId = layer == DataFilterLayer.System ? null : _setup.TenantId,
            EntitySet = set, Layer = layer, LayerKey = key, Query = query, AppliesToDetails = appliesToDetails, Version = 1,
        });
        await db.SaveChangesAsync(Ct);
    }

    private async Task LinkAsync(string set, string property, string target)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        db.Add(new DataFilterLink { EntitySet = set, Property = property, TargetSet = target });
        await db.SaveChangesAsync(Ct);
    }

    private async Task<Guid[]> VisibleIdsAsync<T>(Guid user)
        where T : class
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var ids = await ODataEntityAccess.VisibleIdsAsync<T>(scope.ServiceProvider, Ct);
        return [.. await ids!.ToListAsync(Ct)];
    }

    private async Task<string[]> FilteredAsync(Guid user, string? filter)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var rows = await ODataEntityAccess.FilteredAsync<Gadget>(scope.ServiceProvider, filter, Ct);
        return [.. (await rows.Select(g => g.Name).ToListAsync(Ct)).Order(StringComparer.Ordinal)];
    }

    private static async Task<string[]> SnapshotAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/client-entities/Gadgets/snapshot",
            new ClientEntitySnapshotRequest([new ClientEntitySnapshotColumn("Id", ClientEntitySnapshotKinds.Guid, true), new ClientEntitySnapshotColumn("Name", ClientEntitySnapshotKinds.Text, true)], null), Ct);
        response.EnsureSuccessStatusCode();
        var path = Path.Combine(Path.GetTempPath(), $"coworkee-datafilter-test-{Guid.NewGuid():N}.db");
        try
        {
            await using (var gzip = new GZipStream(await response.Content.ReadAsStreamAsync(Ct), CompressionMode.Decompress))
            await using (var file = File.Create(path))
            {
                await gzip.CopyToAsync(file, Ct);
            }

            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Name FROM rows ORDER BY Name";
            await using var reader = await command.ExecuteReaderAsync(Ct);
            var names = new List<string>();
            while (await reader.ReadAsync(Ct))
            {
                names.Add(reader.GetString(0));
            }

            return [.. names];
        }
        finally
        {
            File.Delete(path);
        }
    }

    private HttpClient Client(Guid user) => app.As(user, _setup.TenantId);

    private async Task<Guid> UserWithAsync(params string[] permissions)
    {
        var admin = Client(_setup.AdminUserId);
        var user = await (await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"df-{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct);
        var role = await (await admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Viewers " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
        (await admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/v1/identity/users/{user!.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return user.Id;
    }
}
