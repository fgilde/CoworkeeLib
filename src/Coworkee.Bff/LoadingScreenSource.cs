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
/// Reads what the server renders into the loading screen from the API: the theme (the one a signed-in user chose when every member of the
/// organisation may choose it, else the organisation's default, the installation's for anonymous visitors) and its texts in the visitor's language,
/// mapped to one of the app's active languages. Cached per organisation and theme, texts per active language; never slower than <see cref="Timeout"/>:
/// without an answer the screen uses neutral colours and English.
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
        var path = "api/v1/themes/loading-screen" + (theme is { } id ? $"?themeId={id}" : string.Empty);

        // the API's answer depends on the organisation and the theme only, never on the user's own rights
        var key = $"coworkee:loading-screen:{context.User.FindFirstValue("tenant")}:{theme}";
        if (!signedIn)
        {
            return await CachedAsync(key, Fresh, () => LoadAsync<LoadingScreenDto>(context, path, false));
        }

        if (cache.TryGetValue(key, out LoadingScreenDto? cached))
        {
            return cached;
        }

        // an expired or revoked token is answered as anonymous: that answer must not stand for the organisation
        var screen = await LoadAsync<LoadingScreenDto>(context, path, true);
        if (screen is { SignedIn: true })
        {
            cache.Set(key, screen, Fresh);
        }

        return screen;
    }

    public async Task<LoadingScreenTexts> TextsAsync(HttpContext context, string culture)
    {
        if (culture.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return LoadingScreenTexts.English;
        }

        // only active languages become cache keys: any Accept-Language variant maps to one of them or to English
        var languages = await CachedAsync("coworkee:loading-screen-languages", Texts, () => LoadAsync<LanguageDto[]>(context, "api/v1/localization/languages", false));
        if (Active(languages, culture) is not { } active || active.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return LoadingScreenTexts.English;
        }

        var texts = await CachedAsync("coworkee:loading-screen-texts:" + active, Texts, async () =>
            await LoadAsync<TextsDto>(context, "api/v1/localization/texts/" + Uri.EscapeDataString(active), false) is { } found ? LoadingScreenTexts.From(active, found.Texts) : null);
        return texts ?? LoadingScreenTexts.English;
    }

    /// <summary>The active language for <paramref name="culture"/>: itself, else its parents (zh-Hant-TW, zh-Hant, zh); null without one.</summary>
    internal static string? Active(IReadOnlyList<LanguageDto>? languages, string culture)
    {
        for (var name = culture; name.Length > 0; name = name.LastIndexOf('-') is var dash and > 0 ? name[..dash] : string.Empty)
        {
            if (languages?.FirstOrDefault(l => string.Equals(l.Culture, name, StringComparison.OrdinalIgnoreCase)) is { } language)
            {
                return language.Culture;
            }
        }

        return null;
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

            // shared answers: only the client's timeout ends the call, never one visitor leaving
            using var response = await http.CreateClient(Client).SendAsync(request, CancellationToken.None);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, CancellationToken.None) : default;
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
