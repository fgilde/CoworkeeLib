using System.Net.Http.Json;
using System.Text.Json;

namespace Coworkee.Client.Blazor.Api;

/// <summary>Base for typed API clients behind the BFF: sends the CSRF header and maps problem details to <see cref="ApiException"/>.</summary>
public abstract class ApiClientBase(HttpClient http)
{
    protected async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
    }

    protected async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, url, body, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
    }

    protected async Task SendAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, url, body, cancellationToken);
    }

    protected async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-CSRF", "1");
        var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return response;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? code = null;
        Dictionary<string, string[]>? errors = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            code = problem.TryGetProperty("code", out var c) ? c.GetString() : null;
            errors = problem.TryGetProperty("errors", out var e) ? e.Deserialize<Dictionary<string, string[]>>() : null;
        }
        catch (JsonException)
        {
        }

        throw new ApiException((int)response.StatusCode, code, errors);
    }
}
