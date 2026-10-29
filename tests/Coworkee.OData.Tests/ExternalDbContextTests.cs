using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Search;
using Coworkee.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

/// <summary>Entities of a second, plain DbContext on a database the app does not own (<see cref="ParcelDbContext"/>).</summary>
public class ExternalDbContextTests(ODataApp app) : IAsyncLifetime
{
    /// <summary>The OData route the requests go to; <see cref="CamelCaseRoute"/> runs the suite again on a route that renames every property.</summary>
    protected virtual string Route => CoworkeeODataModule.RoutePrefix;

    private HttpClient As(Guid userId, Guid tenantId) => app.As(userId, tenantId, Route);

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await InParcelsAsync(async db =>
        {
            var old = DateTime.UtcNow.AddDays(-1);
            db.AddRange(
                new Parcel { Name = "Pallet", Owner = "Acme", DateCreatedUtc = old },
                new Parcel { Name = "Crate", Owner = "Acme", DateCreatedUtc = old },
                new Parcel { Name = "Box", Owner = "Secret", DateCreatedUtc = old });
            await db.SaveChangesAsync(Ct);
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => As(_setup.AdminUserId, _setup.TenantId);

    private async Task InParcelsAsync(Func<ParcelDbContext, Task> action)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<ParcelDbContext>());
    }

    [Fact]
    public async Task The_odata_set_reads_the_external_context_through_its_row_filters()
    {
        var page = await Admin.GetFromJsonAsync<JsonElement>("/odata/Parcels?$count=true&$orderby=Name", Ct);

        page.GetProperty("value").EnumerateArray().Select(p => p.GetProperty("Name").GetString()).ShouldBe(["Crate", "Pallet"]);
        page.GetProperty("@odata.count").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task A_computed_property_of_an_external_set_filters_sorts_and_comes_back_when_selected()
    {
        var page = await Admin.GetFromJsonAsync<JsonElement>("/odata/Parcels?$select=Name,Label&$filter=endswith(Label, '(Acme)')&$orderby=Label desc", Ct);

        page.GetProperty("value").EnumerateArray().Select(p => p.GetProperty("Label").GetString()).ShouldBe(["Pallet (Acme)", "Crate (Acme)"]);
        (await Admin.GetFromJsonAsync<JsonElement>("/odata/Parcels?$top=1", Ct)).GetProperty("value")[0].TryGetProperty("Label", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Without_the_permission_the_set_its_feed_and_export_are_forbidden()
    {
        var email = $"viewer-{Guid.NewGuid():N}@acme.test";
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);
        var viewer = As(user!.Id, _setup.TenantId);

        (await viewer.GetAsync("/odata/Parcels", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.GetAsync("/api/v1/client-entities/Parcels/changes", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.GetAsync("/api/v1/data/Parcels/export", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Export_writes_the_visible_external_rows()
    {
        using var workbook = new XLWorkbook(await Admin.GetStreamAsync("/api/v1/data/Parcels/export", Ct));

        workbook.Worksheet(1).RowsUsed().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Global_search_finds_external_rows()
    {
        var result = await Admin.GetFromJsonAsync<GlobalSearchResult>("/api/v1/search?types=parcels&q=pallet", Ct);

        result!.Hits.ShouldHaveSingleItem().Title.ShouldBe("Pallet");
    }

    [Fact]
    public async Task The_pipeline_saves_the_external_and_the_app_context()
    {
        Guid id;
        using (CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId)))
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            id = (await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new ShipParcelCommand("Drum"), Ct)).Value;
        }

        await InParcelsAsync(async db => (await db.Set<Parcel>().SingleAsync(p => p.Id == id, Ct)).Name.ShouldBe("Drum"));
        (await app.InDbAsync(_setup.TenantId, db => db.Set<Gadget>().CountAsync(g => g.Name == "Drum", Ct))).ShouldBe(1);
    }

    [Fact]
    public async Task Client_entity_changes_come_from_the_configured_date_columns()
    {
        var start = await ChangesAsync(null);
        Guid changed = default;
        await InParcelsAsync(async db =>
        {
            var crate = await db.Set<Parcel>().SingleAsync(p => p.Name == "Crate", Ct);
            crate.DateUpdatedUtc = DateTime.UtcNow;
            changed = crate.Id;
            await db.SaveChangesAsync(Ct);
        });

        var delta = await ChangesAsync(start.Version);

        delta.Reset.ShouldBeFalse();
        delta.Changed.ShouldBe([changed]);
        delta.Count.ShouldBe(2);
    }

    private async Task<ClientEntityChanges> ChangesAsync(DateTimeOffset? since) =>
        (await Admin.GetFromJsonAsync<ClientEntityChanges>(
            "/api/v1/client-entities/Parcels/changes" + (since is null ? "" : $"?since={Uri.EscapeDataString(since.Value.ToString("O"))}"), Ct))!;
}
