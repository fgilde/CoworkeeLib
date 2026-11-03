using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace Coworkee.Workflows.Elsa.Activities;

/// <summary>
/// Reads an OData entity set of the app over HTTP with the API key of <c>Coworkee:Workflows:Elsa:ApiKey</c> (a personal key of a service user),
/// so the key owner's permissions apply.
/// </summary>
[Activity("Coworkee", "Coworkee", "Queries an OData entity set of the app, e.g. Products with $filter=Price gt 100.")]
public sealed class QueryOData : CodeActivity
{
    public const string HttpClientName = "coworkee-elsa-odata";

    [Input(Description = "The entity set, e.g. Products.")]
    public Input<string> EntitySet { get; set; } = default!;

    [Input(Description = "The query options, e.g. $filter=Price gt 100&$top=10.")]
    public Input<string?> Query { get; set; } = default!;

    [Output(Description = "The rows (the value array of the answer).")]
    public Output<JsonElement> Rows { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var http = context.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
        var url = $"odata/{Uri.EscapeDataString(context.Get(EntitySet)!)}" + (context.Get(Query) is { Length: > 0 } query ? "?" + query.TrimStart('?') : string.Empty);
        var answer = await http.GetFromJsonAsync<JsonElement>(url, context.CancellationToken);
        context.Set(Rows, answer.TryGetProperty("value", out var rows) ? rows : answer);
    }
}
