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

    public async Task<byte[]> ExportAsync(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/data/{Uri.EscapeDataString(entitySet)}/export{(query with { Count = false }).ToQueryString()}");
        request.Headers.Add("X-CSRF", "1");
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException((int)response.StatusCode, "export.failed", null);
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<ImportResult> ImportAsync(string entitySet, Stream workbook, string fileName, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/data/{Uri.EscapeDataString(entitySet)}/import")
        {
            Content = new MultipartFormDataContent { { new StreamContent(workbook), "file", fileName } },
        };
        request.Headers.Add("X-CSRF", "1");
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException((int)response.StatusCode, "import.failed", null);
        }

        return (await response.Content.ReadFromJsonAsync<ImportResult>(Json, cancellationToken))!;
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
