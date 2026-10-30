using System.Net;
using Coworkee.Contracts.Identity;
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

    private async Task<HttpStatusCode> GetAsync(WebApplication web, string query) =>
        (await web.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId).GetAsync($"/odata/Gadgets?{query}", Ct)).StatusCode;

    private async Task<Result<ODataQueryResult<Gadget>>> QueryAsync(WebApplication web, string query)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = web.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IODataQueries>().QueryAsync<Gadget>("rt/odata", "Gadgets", query, cancellationToken: Ct);
    }
}
