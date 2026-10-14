using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Coworkee.Bff;

/// <summary>
/// Reads what the server renders into the loading screen from the API: the theme (the one a signed-in user chose when the organisation allows it,
/// else the organisation's default, the installation's for anonymous visitors) and its texts in the visitor's language. Cached per organisation
/// and theme, texts per language; never slower than <see cref="Timeout"/>: without an answer the screen uses neutral colours and English.
/// </summary>
public sealed partial class LoadingScreenSource(IHttpClientFactory http, IMemoryCache cache, ILogger<LoadingScreenSource> logger)
{
    internal const string Client = "coworkee-loading-screen";

    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    // theme changes reach the loading screen within this time
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Texts = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    /// <param name="themeId">The theme the visitor chose; only asked for when signed in, the API decides whether it applies.</param>
    public async Task<LoadingScreenDto?> GetAsync(HttpContext context, Guid? themeId = null)
    {
        var signedIn = context.User.Identity?.IsAuthenticated == true;
        var theme = signedIn ? themeId : null;
        // the API's answer depends on the organisation and the theme only, never on the user's own rights
        return await CachedAsync($"coworkee:loading-screen:{context.User.FindFirstValue("tenant")}:{theme}", Fresh,
            () => LoadAsync<LoadingScreenDto>(context, "api/v1/themes/loading-screen" + (theme is { } id ? $"?themeId={id}" : string.Empty), signedIn));
    }

    public async Task<LoadingScreenTexts> TextsAsync(HttpContext context, string culture)
    {
        if (culture.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return LoadingScreenTexts.English;
        }

        var texts = await CachedAsync("coworkee:loading-screen-texts:" + culture, Texts, async () =>
        {
            var found = await LoadAsync<TextsDto>(context, "api/v1/localization/texts/" + Uri.EscapeDataString(culture), false);
            var translated = found is null ? null : LoadingScreenTexts.From(culture, found.Texts);
            if (translated is not { IsTranslated: true } && culture.Split('-') is [var neutral, ..] && neutral != culture)
            {
                found = await LoadAsync<TextsDto>(context, "api/v1/localization/texts/" + Uri.EscapeDataString(neutral), false);
                translated = found is null ? null : LoadingScreenTexts.From(neutral, found.Texts);
            }

            return translated;
        });
        return texts ?? LoadingScreenTexts.English;
    }

    private async Task<T?> CachedAsync<T>(string key, TimeSpan fresh, Func<Task<T?>> load)
        where T : class =>
        await cache.GetOrCreateAsync(key, async entry =>
        {
            var value = await load();
            entry.AbsoluteExpirationRelativeToNow = value is null ? Retry : fresh;
            return value;
        });

    private async Task<T?> LoadAsync<T>(HttpContext context, string path, bool withToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (withToken && await context.GetTokenAsync("access_token") is { } token)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await http.CreateClient(Client).SendAsync(request, context.RequestAborted);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, context.RequestAborted) : default;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Unavailable(logger, path, exception);
            return default;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The loading screen falls back to neutral colours or English: the API did not answer {Path}.")]
    private static partial void Unavailable(ILogger logger, string path, Exception exception);
}
