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

            if (query.FacetsOfAllRows)
            {
                request.Headers.Add(FacetGroupDto.AllRowsHeader, "true");
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

    public async Task<IReadOnlyList<ODataGroup>> GroupAsync<T>(string entitySet, string? filter, IReadOnlyList<string> by, CancellationToken cancellationToken = default)
    {
        var groupBy = $"groupby(({string.Join(",", by.Select(p => p.Replace('.', '/')))}),aggregate($count as Count))";
        var apply = string.IsNullOrWhiteSpace(filter) ? groupBy : $"filter({filter})/{groupBy}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"odata/{Uri.EscapeDataString(entitySet)}?$apply={Uri.EscapeDataString(apply)}");
        request.Headers.Add("X-CSRF", "1");
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException((int)response.StatusCode, "odata.failed", null);
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var types = by.Select(p => PropertyPath.TypeOf(typeof(T), p) ?? throw new ArgumentException($"{typeof(T).Name} has no property {p}.", nameof(by))).ToList();
        return [.. document.RootElement.GetProperty("value").EnumerateArray().Select(row => new ODataGroup(
            [.. by.Select((path, i) => Value(row, path, types[i]))], row.GetProperty("Count").GetInt64()))];
    }

    // a path of navigations comes back nested ({"Customer":{"Name":"Acme"}}); a null step means a null value
    private static object? Value(JsonElement row, string path, Type type)
    {
        var element = row;
        foreach (var name in path.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !TryGetProperty(element, name, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.Null ? null : element.Deserialize(type, Values);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static readonly JsonSerializerOptions Values = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

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
