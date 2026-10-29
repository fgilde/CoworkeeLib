using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

/// <summary><see cref="IODataQueries"/> answers what <c>GET /{route}/{set}</c> answers the same user: rows, count, facets, shapes and refusals.</summary>
public sealed class ODataQueriesTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _viewer;
    private Guid _binViewer;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            var tenant = _setup.TenantId;
            db.AddRange(
                new Gadget { Name = "Drill", Category = "Tools", Price = 99, SerialCode = "SN-777", TenantId = tenant },
                new Gadget { Name = "Hammer", Category = "Tools", Price = 19, TenantId = tenant },
                new Gadget { Name = "Lamp", Category = "Light", Price = 35, TenantId = tenant },
                new Gadget { Name = "Vault", Category = "Secret", Price = 1000, TenantId = tenant });
            var plain = new Bin { Name = "Plain", Size = 1, TenantId = tenant };
            var secret = new Bin { Name = "Secret", Size = 9, TenantId = tenant };
            var open = new Shelf { Name = "Open", TenantId = tenant, Bins = [plain, secret] };
            var other = new Shelf { Name = "Other", TenantId = tenant };
            db.AddRange(open, other);
            await db.SaveChangesAsync();
            (open.Featured, other.Featured) = (plain, secret);
            return await db.SaveChangesAsync();
        });
        _viewer = await UserWithAsync(ODataApp.ViewGadgets);
        _binViewer = await UserWithAsync(ODataApp.ViewGadgets, ODataApp.ViewBins);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Theory]
    [InlineData("odata", "$filter=Price gt 1&$orderby=Name&$count=true&$top=2&$search=o")]
    [InlineData("rt/odata", "$filter=price gt 1&$orderby=name&$count=true&$top=2&$search=o")]
    [InlineData("alias/odata", "$filter=Price gt 1&$orderby=Name&$count=true&$top=2&$skip=1")]
    public async Task Rows_count_and_facets_are_the_routes(string route, string query)
    {
        foreach (var user in new[] { _setup.AdminUserId, _viewer })
        {
            var (names, count, facets) = await HttpAsync(user, $"/{route}/Gadgets?{query}");
            var result = (await InProcessAsync<Gadget>(user, route, "Gadgets", query, facets: true)).Ok();

            result.Shaped.ShouldBeNull();
            result.Rows.Select(g => g.Name).ShouldBe(names, $"{route} {user}");
            result.Count.ShouldBe(count);
            JsonSerializer.Serialize(result.Facets).ShouldBe(JsonSerializer.Serialize(facets));
            result.Rows.ShouldAllBe(g => g.SerialCode == null, "hidden for everyone");
            if (user == _viewer)
            {
                result.Rows.ShouldAllBe(g => g.Category == null, "protected: left at its default");
                result.Facets!.ShouldNotContain(f => f.Key == "category");
            }
            else
            {
                result.Rows.ShouldAllBe(g => g.Category != null);
            }
        }
    }

    [Fact]
    public async Task Shaped_rows_carry_the_routes_names_and_leave_out_what_the_user_may_not_read()
    {
        const string query = "$expand=featured($select=name,size)&$orderby=name&$select=name";
        var shaped = (await InProcessAsync<Shelf>(_setup.AdminUserId, "rt/odata", "Shelves", query)).Ok().Shaped!;
        shaped.Select(r => r["name"]).ShouldBe(["Open", "Other"]);
        ((IReadOnlyDictionary<string, object?>)shaped[0]["featured"]!)["size"].ShouldBe(1);
        shaped[1]["featured"].ShouldBeNull("the secret bin stays hidden");

        // the protected size leaves the expanded bin, as the serializer leaves it out
        var bins = (await InProcessAsync<Shelf>(_binViewer, "rt/odata", "Shelves", "$expand=featured&$orderby=name")).Ok().Shaped!;
        var featured = (IReadOnlyDictionary<string, object?>)bins[0]["featured"]!;
        featured["name"].ShouldBe("Plain");
        featured.Keys.ShouldNotContain("size");
        var http = await app.As(_binViewer, _setup.TenantId).GetFromJsonAsync<JsonElement>("/rt/odata/Shelves?$expand=featured&$orderby=name", Ct);
        featured.Keys.ShouldBe(http.GetProperty("value")[0].GetProperty("featured").EnumerateObject().Select(p => p.Name), ignoreOrder: true);
    }

    [Fact]
    public async Task Refuses_what_the_route_refuses()
    {
        (await InProcessAsync<Gadget>(_viewer, "rt/odata", "Gadgets", "$filter=category eq 'Tools'")).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await InProcessAsync<Gadget>(_viewer, "alias/odata", "Gadgets", "$orderby=Kind")).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await InProcessAsync<Shelf>(_viewer, "odata", "Shelves", "$expand=Featured")).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await InProcessAsync<Gadget>(_setup.AdminUserId, "rt/odata", "Gadgets", "$filter=nope eq 1")).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await InProcessAsync<Gadget>(_setup.AdminUserId, "odata", "Gadgets", "$top=100000")).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await InProcessAsync<Gadget>(_setup.AdminUserId, "odata", "Gadgets", "@p=Category&$filter=@p eq 'x'")).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await InProcessAsync<Bin>(_viewer, "odata", "Bins", null)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await InProcessAsync<Gadget>(null, "odata", "Gadgets", null)).Error!.Kind.ShouldBe(ErrorKind.Unauthorized);
        (await InProcessAsync<Gadget>(_viewer, "odata", "Gadgets", "$filter=Category eq 'Tools'")).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Members_behind_fields_are_cleared_on_typed_rows()
    {
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(Locket.Create("A", "note a", "code a", _setup.TenantId), Locket.Create("B", "note b", "code b", _setup.TenantId));
            return await db.SaveChangesAsync();
        });

        // by convention neither getter is in the model: both are cleared, through the fields EF reads them from
        var rows = (await InProcessAsync<Locket>(_setup.AdminUserId, "odata", "Lockets", "$orderby=Name")).Ok().Rows;
        rows.Select(l => (l.Name, l.Note, l.Code)).ShouldBe([("A", null, null), ("B", null, null)]);

        // from the EF model the protected one is in it: kept for users with the permission, cleared for others; the hidden one never shows
        await using var efApp = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:ModelFromEntityFramework"] = "true" });
        (await InProcessAsync<Locket>(_setup.AdminUserId, "odata", "Lockets", "$orderby=Name", efApp.Services)).Ok().Rows
            .Select(l => (l.Note, l.Code)).ShouldBe([(null, "code a"), (null, "code b")]);
        (await InProcessAsync<Locket>(_viewer, "odata", "Lockets", "$orderby=Name", efApp.Services)).Ok().Rows
            .Select(l => (l.Note, l.Code)).ShouldBe([(null, null), (null, null)]);
    }

    [Fact]
    public async Task An_entity_with_a_member_that_cannot_be_cleared_comes_back_shaped()
    {
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.Add(new Ledger { Name = "Books", TenantId = _setup.TenantId });
            return await db.SaveChangesAsync();
        });

        var page = (await InProcessAsync<Ledger>(_setup.AdminUserId, "rt/odata", "Ledgers", null)).Ok();

        page.Rows.ShouldBeEmpty();
        page.Shaped!.Single()["name"].ShouldBe("Books");
        page.Shaped!.Single().Keys.ShouldNotContain("summary");
        page.Shaped!.Single().Keys.ShouldNotContain("Summary");
    }

    [Fact]
    public async Task A_query_the_database_cannot_run_fails_without_its_message()
    {
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.Add(Locket.Create("A", "n", "c", _setup.TenantId));
            return await db.SaveChangesAsync();
        });

        var error = (await InProcessAsync<Locket>(_setup.AdminUserId, "odata", "Lockets", "$filter=Peek eq 'A'")).Error!;

        error.Kind.ShouldBe(ErrorKind.Validation);
        error.Message.ShouldBe("The query could not be run.");
        app.Logs.Warnings.ShouldContain(w => w.Contains("in-process OData query on odata/Lockets"));
    }

    [Fact]
    public async Task The_page_size_of_the_model_pages_like_the_route()
    {
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(Locket.Create("A", "n", "c", _setup.TenantId), Locket.Create("B", "n", "c", _setup.TenantId), Locket.Create("C", "n", "c", _setup.TenantId));
            return await db.SaveChangesAsync();
        });

        var http = await Admin.GetFromJsonAsync<JsonElement>("/odata/Lockets?$orderby=Name&$count=true", Ct);
        var page = (await InProcessAsync<Locket>(_setup.AdminUserId, "odata", "Lockets", "$orderby=Name&$count=true")).Ok();

        http.GetProperty("value").GetArrayLength().ShouldBe(2);
        http.TryGetProperty("@odata.nextLink", out _).ShouldBeTrue();
        page.Rows.Select(l => l.Name).ShouldBe(["A", "B"]);
        page.Count.ShouldBe(3);
    }

    [Fact]
    public async Task The_group_selection_narrows_the_rows_like_the_list()
    {
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(new Crate { Name = "Box A", UnitId = FakeGroupScope.A, TenantId = _setup.TenantId }, new Crate { Name = "Box B", UnitId = FakeGroupScope.B, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync();
        });
        app.App.Services.GetRequiredService<FakeGroupScope>().Stored = [FakeGroupScope.A];

        var http = await Admin.GetFromJsonAsync<JsonElement>("/odata/Crates?$count=true", Ct);
        using (Coworkee.Application.Groups.GroupScope.Selection([FakeGroupScope.A]))
        {
            var page = (await InProcessAsync<Crate>(_setup.AdminUserId, "odata", "Crates", "$count=true")).Ok();
            page.Rows.Select(c => c.Name).ShouldBe(["Box A"]);
            page.Count.ShouldBe(http.GetProperty("@odata.count").GetInt64());
        }

        (await InProcessAsync<Crate>(_setup.AdminUserId, "odata", "Crates", "$count=true")).Ok().Count.ShouldBe(2, "no selection outside a request");
    }

    private async Task<Result<ODataQueryResult<T>>> InProcessAsync<T>(Guid? user, string route, string set, string? query, IServiceProvider? services = null, bool facets = false)
        where T : class
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = (services ?? app.App.Services).CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IODataQueries>().QueryAsync<T>(route, set, query, facets, Ct);
    }

    private async Task<(string[] Names, long? Count, List<FacetGroupDto> Facets)> HttpAsync(Guid user, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");
        using var response = await app.As(user, _setup.TenantId).SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var page = JsonDocument.Parse(body).RootElement;
        var names = page.GetProperty("value").EnumerateArray().Select(r => (r.TryGetProperty("Name", out var name) ? name : r.GetProperty("name")).GetString()!).ToArray();
        return (names, page.TryGetProperty("@odata.count", out var count) ? count.GetInt64() : null,
            page.GetProperty("@cn.facets").Deserialize<List<FacetGroupDto>>(JsonSerializerOptions.Web)!);
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

internal static class ResultAssertions
{
    public static T Ok<T>(this Result<T> result) => result.IsSuccess ? result.Value : throw new ShouldAssertException($"{result.Error!.Kind}: {result.Error.Message}");
}
