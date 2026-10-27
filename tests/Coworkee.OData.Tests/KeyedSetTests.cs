using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Search;
using Coworkee.Core.Security;
using Coworkee.OData.Transfer;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

/// <summary>Sets keyed by another property than a Guid Id: Countries by a text, Ranks by a number.</summary>
public sealed class KeyedSetTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(
                new Country { IsoCode = "DE", Name = "Germany", Continent = "Europe" },
                new Country { IsoCode = "FR", Name = "France", Continent = "Europe" },
                new Country { IsoCode = "JP", Name = "Japan", Continent = "Asia" },
                new Rank { Number = 1, Title = "Bronze" },
                new Rank { Number = 2, Title = "Silver" });
            return await db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task A_text_key_lists_counts_and_reads_by_key()
    {
        var page = await Admin.GetFromJsonAsync<JsonElement>("/odata/Countries?$filter=Continent eq 'Europe'&$orderby=Name&$count=true", Ct);
        page.GetProperty("@odata.count").GetInt32().ShouldBe(2);
        page.GetProperty("value").EnumerateArray().Select(c => c.GetProperty("IsoCode").GetString()).ShouldBe(["FR", "DE"]);

        (await Admin.GetFromJsonAsync<JsonElement>("/odata/Countries('JP')", Ct)).GetProperty("Name").GetString().ShouldBe("Japan");
        (await Admin.GetAsync("/odata/Countries('XX')", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_number_key_lists_and_reads_by_key()
    {
        (await Admin.GetFromJsonAsync<JsonElement>("/odata/Ranks?$orderby=Number desc", Ct)).GetProperty("value")[0].GetProperty("Title").GetString().ShouldBe("Silver");
        (await Admin.GetFromJsonAsync<JsonElement>("/odata/Ranks(2)", Ct)).GetProperty("Title").GetString().ShouldBe("Silver");
    }

    [Fact]
    public async Task Facets_search_export_and_the_data_tools_work_on_a_keyed_set()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Countries");
        request.Headers.Add("Prefer", "odata.include-annotations=\"*\"");
        (await (await Admin.SendAsync(request, Ct)).Content.ReadAsStringAsync(Ct)).ShouldContain("Asia");

        var hits = await Admin.GetFromJsonAsync<GlobalSearchResult>("/api/v1/search?types=countries&q=germ", Ct);
        hits!.Hits.Single().Id.ShouldBe("DE");

        (await Admin.GetStringAsync("/api/v1/data/Countries/export?format=csv&$orderby=IsoCode", Ct)).ShouldContain("Germany");

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new QueryDataQuery("Ranks", "Number gt 1"), Ct)).Value.Rows.Single()["Title"].ShouldBe("Silver");
    }

    [Fact]
    public async Task Features_that_address_rows_by_a_guid_id_refuse_or_skip_the_entity()
    {
        Should.Throw<InvalidOperationException>(() => ODataEntityAccess.RequireGuidId<Country>("Social")).Message.ShouldContain("Guid Id");
        await Should.ThrowAsync<InvalidOperationException>(() => ODataEntityAccess.VisibleIdsAsync<Rank>(app.App.Services, Ct));

        // client entities leave keyed sets on the server, also when asked for
        app.App.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ClientEntityOptions>>().Value.For<Country>();
        (await Admin.GetAsync("/api/v1/client-entities/Countries/changes", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Imports_that_update_refuse_a_set_keyed_otherwise_whatever_the_order()
    {
        var entityFirst = new ServiceCollection();
        entityFirst.AddODataEntity<Rank>("Ranks", null, o => o.Key(r => r.Number));
        entityFirst.AddODataImport<AddGadgetCommand>("Ranks"); // creating only needs no key
        Should.Throw<InvalidOperationException>(() => entityFirst.AddODataImport("Ranks", (Guid? id, WidgetRow row) => new SaveWidgetCommand(id, row)))
            .Message.ShouldContain("keyed by Number");

        var importFirst = new ServiceCollection();
        importFirst.AddODataImport("Ranks", (Guid? id, WidgetRow row) => new SaveWidgetCommand(id, row));
        Should.Throw<InvalidOperationException>(() => importFirst.AddODataEntity<Rank>("Ranks", null, o => o.Key(r => r.Number)));
    }
}
