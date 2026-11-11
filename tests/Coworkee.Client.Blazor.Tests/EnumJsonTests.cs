using System.Net;
using System.Text;
using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The server sends enums by name (OData always, the API through its JSON options); the clients read names and numbers.</summary>
public sealed class EnumJsonTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OData_rows_read_enum_names_and_numbers_also_flags()
    {
        var handler = new Answer("""
            {"@odata.count":3,"value":[
              {"Id":1,"Status":"Shipped","Colors":"Red, Blue"},
              {"Id":2,"Status":1,"Colors":5},
              {"Id":3,"Status":"Open","Colors":"None"}]}
            """);
        var client = new ODataClient(new HttpClient(handler) { BaseAddress = new Uri("http://app/") });

        var page = await client.QueryAsync<Row>("Rows", new ODataQuery(), Ct);

        page.Items.ShouldBe([new Row(1, Status.Shipped, Colors.Red | Colors.Blue), new Row(2, Status.Shipped, Colors.Red | Colors.Blue), new Row(3, Status.Open, Colors.None)]);
    }

    [Fact]
    public async Task Api_clients_write_enum_names_and_read_names_and_numbers()
    {
        var handler = new Answer("""{"id":7,"status":"Shipped","colors":"Red, Blue"}""");
        var api = new RowsApi(new HttpClient(handler) { BaseAddress = new Uri("http://app/") });

        var saved = await api.SaveAsync(new Row(7, Status.Shipped, Colors.Red | Colors.Blue));

        handler.Body.ShouldBe("""{"id":7,"status":"Shipped","colors":"Red, Blue"}""");
        saved.ShouldBe(new Row(7, Status.Shipped, Colors.Red | Colors.Blue));
        JsonSerializer.Deserialize<Row>("""{"id":7,"status":1,"colors":5}""", CoworkeeJson.Web).ShouldBe(saved);
    }

    [Fact]
    public void The_shared_options_cannot_be_changed_by_accident() =>
        Should.Throw<InvalidOperationException>(() => CoworkeeJson.Web.Converters.Clear());

    public enum Status
    {
        Open,
        Shipped,
    }

    [Flags]
    public enum Colors
    {
        None = 0,
        Red = 1,
        Green = 2,
        Blue = 4,
    }

    public sealed record Row(int Id, Status Status, Colors Colors);

    private sealed class RowsApi(HttpClient http) : ApiClientBase(http)
    {
        public Task<Row> SaveAsync(Row row) => SendAsync<Row>(HttpMethod.Post, "api/v1/rows", row, Ct);
    }

    private sealed class Answer(string json) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
