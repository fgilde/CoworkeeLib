using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

internal sealed class ODataClient(HttpClient http) : IODataClient
{
    private const string FacetsAnnotation = "@cn.facets";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"odata/{Uri.EscapeDataString(entitySet)}{query.ToQueryString()}");
        request.Headers.Add("X-CSRF", "1");
        if (query.Facets)
        {
            request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");
        }

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException((int)response.StatusCode, "odata.failed", null);
        }

        var page = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var items = page.GetProperty("value").Deserialize<List<T>>(Json) ?? [];
        long? count = page.TryGetProperty("@odata.count", out var total) ? total.GetInt64() : null;
        var facets = page.TryGetProperty(FacetsAnnotation, out var groups) ? groups.Deserialize<List<FacetGroupDto>>(Json) ?? [] : [];
        return new ODataPage<T>(items, count, facets);
    }
}
