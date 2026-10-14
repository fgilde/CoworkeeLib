using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Coworkee.Bff;

/// <summary>
/// Reads the API's loading screen (default theme of the signed-in user's organisation, else the installation's) for the page the server
/// renders; cached per organisation, and never slower than <see cref="Timeout"/>: without an answer the screen uses neutral colours.
/// </summary>
public sealed partial class LoadingScreenSource(IHttpClientFactory http, IMemoryCache cache, ILogger<LoadingScreenSource> logger)
{
    internal const string Client = "coworkee-loading-screen";

    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    // theme changes reach the loading screen within this time
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    public async Task<LoadingScreenDto?> GetAsync(HttpContext context) =>
        await cache.GetOrCreateAsync("coworkee:loading-screen:" + context.User.FindFirstValue("tenant"), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Fresh;
            var screen = await LoadAsync(context);
            if (screen is null)
            {
                entry.AbsoluteExpirationRelativeToNow = Retry;
            }

            return screen;
        });

    private async Task<LoadingScreenDto?> LoadAsync(HttpContext context)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/themes/loading-screen");
            if (context.User.Identity?.IsAuthenticated == true && await context.GetTokenAsync("access_token") is { } token)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await http.CreateClient(Client).SendAsync(request, context.RequestAborted);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<LoadingScreenDto>(JsonSerializerOptions.Web, context.RequestAborted) : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Unavailable(logger, exception);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The loading screen falls back to neutral colours: the API did not answer.")]
    private static partial void Unavailable(ILogger logger, Exception exception);
}
