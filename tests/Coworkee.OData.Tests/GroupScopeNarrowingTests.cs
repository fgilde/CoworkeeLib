using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

/// <summary>The selected units narrow what lists show, never what a user may open.</summary>
public class GroupScopeNarrowingTests(ODataApp app) : IAsyncLifetime
{
    /// <summary>The OData route the requests go to; <see cref="CamelCaseRoute"/> runs the suite again on a route that renames every property.</summary>
    protected virtual string Route => CoworkeeODataModule.RoutePrefix;

    private HttpClient As(Guid userId, Guid tenantId) => app.As(userId, tenantId, Route);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private SetupResultDto _setup = null!;
    private Guid _boxB;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _boxB = await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(Enumerable.Range(1, 6).Select(i => new Crate { Name = $"Box A{i}", UnitId = FakeGroupScope.A, TenantId = _setup.TenantId }));
            var b = new Crate { Name = "Box B", UnitId = FakeGroupScope.B, TenantId = _setup.TenantId };
            db.Add(b);
            await db.SaveChangesAsync();
            return b.Id;
        });
    }

    public ValueTask DisposeAsync()
    {
        Root.Held = null;
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => As(_setup.AdminUserId, _setup.TenantId);

    private FakeGroupScope Scope => app.App.Services.GetRequiredService<FakeGroupScope>();

    private InlineBackgroundJobs.Root Root => app.App.Services.GetRequiredService<InlineBackgroundJobs.Root>();

    [Fact]
    public async Task Collection_follows_the_selection()
    {
        Scope.Stored = [FakeGroupScope.A];

        var page = await Admin.GetFromJsonAsync<JsonElement>("/odata/Crates?$count=true", Ct);

        page.GetProperty("value").EnumerateArray().Select(c => c.GetProperty("Name").GetString()).ShouldAllBe(n => n!.StartsWith("Box A"));
        page.GetProperty("@odata.count").GetInt32().ShouldBe(6);
    }

    [Fact]
    public async Task By_key_read_ignores_the_selection()
    {
        Scope.Stored = [FakeGroupScope.A];

        var response = await Admin.GetAsync($"/odata/Crates({_boxB})", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Admin.GetAsync($"/odata/Crates({Guid.NewGuid()})", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Global_search_follows_the_selection()
    {
        Scope.Stored = [FakeGroupScope.B];

        var result = await Admin.GetFromJsonAsync<GlobalSearchResult>("/api/v1/search?types=crates&q=box", Ct);

        result!.Hits.Select(h => h.Title).ShouldBe(["Box B"]);
    }

    [Fact]
    public async Task Export_uses_the_selection_of_the_request()
    {
        Scope.Stored = [FakeGroupScope.A];
        Root.Held = [];

        var response = await Admin.GetAsync("/api/v1/data/Crates/export?format=json", Ct);
        Scope.Stored = [FakeGroupScope.B];
        foreach (var job in Root.Held)
        {
            await job();
        }

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var id = (await response.Content.ReadFromJsonAsync<DataTransferJobDto>(Json, Ct))!.Id;
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(await Admin.GetByteArrayAsync($"/api/v1/data/jobs/{id}/file", Ct), Json)!;
        rows.Select(r => r["Name"].GetString()).ShouldBe(Enumerable.Range(1, 6).Select(i => $"Box A{i}"), ignoreOrder: true);
    }

    [Fact]
    public async Task Import_matches_rows_outside_the_selection()
    {
        Scope.Stored = [FakeGroupScope.A];
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("Name;Quantity\r\nBox B;7\r\n")), "file", "crates.csv" } };
        var sheet = (await (await Admin.PostAsync("/api/v1/data/Crates/import/file", form, Ct)).Content.ReadFromJsonAsync<ImportSheetDto>(Json, Ct))!;

        var response = await Admin.PostAsJsonAsync($"/api/v1/data/imports/{sheet.JobId}/start", new ImportSettings(["Name", "Quantity"], ImportMode.Upsert, "Name"), Json, Ct);

        var job = (await response.Content.ReadFromJsonAsync<DataTransferJobDto>(Json, Ct))!;
        (job.Created, job.Updated, job.Failed).ShouldBe((0, 1, 0));
        var boxes = await app.InDbAsync(_setup.TenantId, db => db.Set<Crate>().Where(c => c.Name == "Box B").ToListAsync(Ct));
        boxes.ShouldHaveSingleItem().Quantity.ShouldBe(7);
    }
}
