using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Settings;

internal sealed partial class AspireUIServiceSource(
    IOptionsMonitor<CoworkeeAspireUIOptions> options, IHttpClientFactory clients, HybridCache cache, ILogger<AspireUIServiceSource> logger) : IServiceSource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private static readonly HybridCacheEntryOptions Entry = new() { Expiration = TimeSpan.FromSeconds(15), LocalCacheExpiration = TimeSpan.FromSeconds(15) };

    public string Name => "aspireui";

    public async Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        if (!Uri.TryCreate(current.Url, UriKind.Absolute, out var url))
        {
            return [];
        }

        return await cache.GetOrCreateAsync("coworkee:aspireui:" + url, async token => await FetchAsync(url, current.ApiKey, token), Entry, cancellationToken: cancellationToken);
    }

    private async Task<ServiceDto[]> FetchAsync(Uri url, string? apiKey, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, "/api/retrieve/apps"));
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        try
        {
            using var response = await clients.CreateClient(ConfiguredServiceSource.Client).SendAsync(request, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
            {
                LogUnavailable(url, (int)response.StatusCode);
                return [];
            }

            var apps = await response.Content.ReadFromJsonAsync<List<RetrievedApp>>(timeout.Token) ?? [];
            return apps.Where(a => !string.IsNullOrEmpty(a.Name)).Select(ToDto).ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException && !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(url, exception.GetType().Name);
            return [];
        }
    }

    private static ServiceDto ToDto(RetrievedApp app)
    {
        var urls = app.Urls ?? [];
        return new ServiceDto(app.Name!, app.Name!, urls.FirstOrDefault() ?? "", app.Healthy ? ServiceHealth.Healthy : ServiceHealth.Unhealthy, "aspireui", urls);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The AspireUI at {Url} gave no apps ({Reason})")]
    private partial void LogUnavailable(Uri url, object reason);

    private sealed record RetrievedApp(string? Name, bool Healthy, List<string>? Urls);
}
