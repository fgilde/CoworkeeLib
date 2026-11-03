using System.Net;
using System.Text;
using Coworkee.Client.Blazor.Data;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ODataClientGroupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Groups_are_counted_by_the_server_with_apply_and_come_back_typed()
    {
        var handler = new Answer("""{"value":[{"Status":"Open","Customer":{"Name":"Acme"},"Count":3},{"Status":"Closed","Customer":null,"Count":1}]}""");
        var client = new ODataClient(new HttpClient(handler) { BaseAddress = new Uri("http://app/") });

        var groups = await client.GroupAsync<Order>("Orders", "Total gt 5", ["Status", "Customer.Name"], Ct);

        handler.Uri.ShouldBe("http://app/odata/Orders?$apply=" + Uri.EscapeDataString("filter(Total gt 5)/groupby((Status,Customer/Name),aggregate($count as Count))"));
        groups.Select(g => (g.Values[0], g.Values[1], g.Count)).ShouldBe([(OrderStatus.Open, "Acme", 3L), (OrderStatus.Closed, null, 1L)]);
    }

    [Fact]
    public async Task Without_a_filter_only_the_grouping_is_sent()
    {
        var handler = new Answer("""{"value":[]}""");
        var client = new ODataClient(new HttpClient(handler) { BaseAddress = new Uri("http://app/") });

        (await client.GroupAsync<Order>("Orders", null, ["Total"], Ct)).ShouldBeEmpty();

        handler.Uri.ShouldBe("http://app/odata/Orders?$apply=" + Uri.EscapeDataString("groupby((Total),aggregate($count as Count))"));
    }

    [Fact]
    public async Task A_client_that_cannot_group_says_so()
    {
        IODataClient client = new Rows();

        await Should.ThrowAsync<NotSupportedException>(() => client.GroupAsync<Order>("Orders", null, ["Status"], Ct));
    }

    public enum OrderStatus
    {
        Open,
        Closed,
    }

    public sealed record Customer(string Name);

    public sealed record Order(Guid Id, OrderStatus Status, Customer? Customer, decimal Total);

    private sealed class Rows : IODataClient
    {
        public Task<Contracts.Data.ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class Answer(string json) : HttpMessageHandler
    {
        public string? Uri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
