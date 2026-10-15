using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

internal sealed class ODataClient(HttpClient http) : IODataClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"odata/{Uri.EscapeDataString(entitySet)}{query.ToQueryString()}");
        request.Headers.Add("X-CSRF", "1");
        if (query.Facets)
        {
            request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");
            if (query.FacetContext is { } context)
            {
                request.Headers.Add(FacetContextDto.Header, context.Encode());
            }
        }

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException((int)response.StatusCode, "odata.failed", null);
        }

        // one pass over the response: large pages, such as the download of client entities, cost no extra document
        var page = await response.Content.ReadFromJsonAsync<Response<T>>(Json, cancellationToken);
        return new ODataPage<T>(page?.Value ?? [], page?.Count, page?.Facets ?? []);
    }

    private sealed class Response<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }

        [JsonPropertyName("@odata.count")]
        public long? Count { get; set; }

        [JsonPropertyName("@cn.facets")]
        public List<FacetGroupDto>? Facets { get; set; }
    }
}
