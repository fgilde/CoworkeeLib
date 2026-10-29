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

    private async Task<Result<ODataQueryResult<T>>> InProcessAsync<T>(Guid? user, string route, string set, string? query, bool facets = false)
        where T : class
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
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
