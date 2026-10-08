using System.Net.Http.Json;
using System.Text.Json;

namespace Coworkee.Client;

/// <summary>
/// Base for a typed API client. Give it an <see cref="HttpClient"/> whose base address is the API and whose requests carry
/// a bearer token, for example through <see cref="BearerTokenHandler"/>.
/// </summary>
public abstract class CoworkeeApiClient(HttpClient http)
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected HttpClient Http => http;

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        await EnsureAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
    }

    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken = default)
    {
        using var response = await SendContentAsync(method, path, Content(body), cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
    }

    public async Task SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken = default)
    {
        using var response = await SendContentAsync(method, path, Content(body), cancellationToken);
    }

    /// <summary>Sends any content, for example a multipart upload; the caller disposes the response.</summary>
    protected async Task<HttpResponseMessage> SendContentAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            await EnsureAsync(response, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    protected static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? title = null;
        string? code = null;
        Dictionary<string, string[]>? errors = null;
        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = problem.RootElement;
            title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
            errors = root.TryGetProperty("errors", out var e) ? e.Deserialize<Dictionary<string, string[]>>(Json) : null;
        }
        catch (JsonException)
        {
        }

        throw new CoworkeeApiException(response.StatusCode, title ?? $"The API answered {(int)response.StatusCode}.", code, errors);
    }

    private static JsonContent? Content(object? body) => body is null ? null : JsonContent.Create(body, body.GetType(), options: Json);
}
