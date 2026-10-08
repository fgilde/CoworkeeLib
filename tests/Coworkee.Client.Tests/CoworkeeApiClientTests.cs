using System.Net;
using System.Text;

namespace Coworkee.Client.Tests;

public sealed class CoworkeeApiClientTests
{
    [Fact]
    public async Task Reads_json_and_turns_problem_details_into_exceptions()
    {
        var client = new TestClient(request => request.RequestUri!.AbsolutePath switch
        {
            "/ok" => Json(HttpStatusCode.OK, """{"name":"Drill"}"""),
            _ => Json(HttpStatusCode.BadRequest, """{"title":"One or more validation errors occurred.","code":"validation","errors":{"Name":["must not be empty"]}}"""),
        });

        (await client.GetAsync<Gadget>("ok", TestContext.Current.CancellationToken)).Name.ShouldBe("Drill");
        var failure = await Should.ThrowAsync<CoworkeeApiException>(() => client.SendAsync(HttpMethod.Post, "fail", new Gadget(""), TestContext.Current.CancellationToken));
        (failure.Status, failure.Code, failure.Errors["Name"][0]).ShouldBe((HttpStatusCode.BadRequest, "validation", "must not be empty"));
    }

    [Fact]
    public async Task Queries_an_entity_set_with_options_and_count()
    {
        Uri? asked = null;
        var client = new TestClient(request =>
        {
            asked = request.RequestUri;
            return Json(HttpStatusCode.OK, """{"@odata.count":7,"value":[{"name":"Drill"}]}""");
        });

        var page = await client.QueryAsync<Gadget>("Gadgets", filter: "Price gt 10", orderBy: "Name", top: 1, cancellationToken: TestContext.Current.CancellationToken);

        (page.Count, page.Items.Single().Name).ShouldBe((7L, "Drill"));
        asked!.PathAndQuery.ShouldBe("/odata/Gadgets?$filter=Price%20gt%2010&$orderby=Name&$top=1&$count=true");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record Gadget(string Name);

    private sealed class TestClient(Func<HttpRequestMessage, HttpResponseMessage> answer)
        : CoworkeeApiClient(new HttpClient(new Handler(answer)) { BaseAddress = new Uri("https://api.test/") });

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(answer(request));
    }
}
