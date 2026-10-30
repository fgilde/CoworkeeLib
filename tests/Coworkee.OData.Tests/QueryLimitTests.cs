using System.Net;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.OData.Transfer;
using Microsoft.AspNetCore.Http;
using Microsoft.OData;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

/// <summary><c>Coworkee:OData:MaxNodeCount</c> raises OData's limit of 100 filter nodes on every route and in <see cref="IODataQueries"/>.</summary>
public sealed class QueryLimitTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // a search over many fields, as a global search box sends it
    private static string Search(int terms) => "$filter=" + string.Join(" or ", Enumerable.Range(0, terms).Select(i => $"contains(Name,'term{i}')"));

    [Fact]
    public async Task The_default_refuses_a_filter_of_more_than_100_nodes()
    {
        (await GetAsync(app.App, Search(5))).ShouldBe(HttpStatusCode.OK);
        (await GetAsync(app.App, Search(40))).ShouldBe(HttpStatusCode.BadRequest);
        (await QueryAsync(app.App, Search(40))).Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task A_raised_limit_takes_larger_filters_and_still_refuses_beyond_it()
    {
        await using var raised = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:MaxNodeCount"] = "1000" });

        (await GetAsync(raised, Search(40))).ShouldBe(HttpStatusCode.OK);
        (await QueryAsync(raised, Search(40))).IsSuccess.ShouldBeTrue();
        (await GetAsync(raised, Search(400))).ShouldBe(HttpStatusCode.BadRequest);
        (await QueryAsync(raised, Search(400))).Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task Every_place_that_reads_a_callers_filter_keeps_the_node_limit()
    {
        var filter = Search(40)["$filter=".Length..];
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);

        // the list with facets, the export, query_data (MCP and the assistant), the filters of code (mail recipients, reports) and facet filters
        using var withFacets = new HttpRequestMessage(HttpMethod.Get, $"/odata/Gadgets?{Search(40)}");
        withFacets.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");
        (await admin.SendAsync(withFacets, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await QueryAsync(app.App, Search(40), facets: true)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await admin.GetAsync($"/api/v1/data/Gadgets/export?{Search(40)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        (await services.GetRequiredService<IDispatcher>().SendAsync(new QueryDataQuery("Gadgets", filter), Ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        await Should.ThrowAsync<ODataException>(() => ODataEntityAccess.FilteredAsync<Gadget>(services, filter, Ct));
        await Should.ThrowAsync<ODataException>(() =>
            FacetResponse.FilterAsync(services.GetRequiredService<GadgetDbContext>().Set<Gadget>(), filter, new DefaultHttpContext { RequestServices = services }, Ct, clrNames: true));
    }

    public static TheoryData<string, string, string> Siblings => new()
    {
        { "Shelves?$filter=Bins/any(b: b/Labels/any(l: l/Text eq 'x'))", "MaxAnyAllExpressionDepth", "2" },
        { "Parts?$expand=Parent($expand=Parent($expand=Parent))", "MaxExpansionDepth", "3" },
        { "Gadgets?$orderby=Name,Price,Id,Category,TenantId,IsDeleted", "MaxOrderByNodeCount", "6" },
    };

    [Theory]
    [MemberData(nameof(Siblings))]
    public async Task The_other_limits_refuse_by_default_and_follow_their_option(string url, string option, string raisedTo)
    {
        (await GetUrlAsync(app.App, url)).ShouldBe(HttpStatusCode.BadRequest);

        await using var raised = await app.StartAnotherAsync(new Dictionary<string, string> { [$"Coworkee:OData:{option}"] = raisedTo });

        (await GetUrlAsync(raised, url)).ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("MaxNodeCount", "0")]
    [InlineData("MaxAnyAllExpressionDepth", "0")]
    [InlineData("MaxOrderByNodeCount", "0")]
    [InlineData("MaxExpansionDepth", "-1")]
    public async Task A_limit_out_of_range_stops_the_start(string option, string value)
    {
        var refused = await Should.ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>(async () =>
        {
            await using var app2 = await app.StartAnotherAsync(new Dictionary<string, string> { [$"Coworkee:OData:{option}"] = value });
        });

        refused.Message.ShouldContain(option);
    }

    private Task<HttpStatusCode> GetAsync(WebApplication web, string query) => GetUrlAsync(web, $"Gadgets?{query}");

    private async Task<HttpStatusCode> GetUrlAsync(WebApplication web, string url) =>
        (await web.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId).GetAsync($"/odata/{url}", Ct)).StatusCode;

    private async Task<Result<ODataQueryResult<Gadget>>> QueryAsync(WebApplication web, string query, bool facets = false)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = web.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IODataQueries>().QueryAsync<Gadget>("rt/odata", "Gadgets", query, facets, Ct);
    }
}
