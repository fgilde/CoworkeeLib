using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Coworkee.Bff;

/// <summary>
/// The loading screen's logo as its own cacheable response (a logo of up to 512 KB would otherwise be inlined in every page): the theme of the
/// visitor's loading screen, versioned by theme and revision in the address, so the browser keeps it until the theme changes.
/// </summary>
internal static class LoadingScreenLogo
{
    public const string Path = "/bff/loading-screen/logo";

    /// <summary>The address of the theme's logo; null when the theme has no readable logo.</summary>
    public static string? Url(ThemeDto? theme) =>
        theme is not null && ThemeLogo.TryRead(theme.Logo, out _, out _) ? $"{Path}?v={Version(theme)}" : null;

    public static async Task<IResult> ServeAsync(HttpContext context, LoadingScreenSource source, string? v)
    {
        var screen = await source.GetAsync(context, LoadingScreenPreferences.Read(context.Request).ThemeId);
        if (screen?.Theme is not { } theme || !ThemeLogo.TryRead(theme.Logo, out var bytes, out var contentType))
        {
            return Results.NotFound();
        }

        var headers = context.Response.Headers;
        // the theme depends on the visitor's session; an outdated address is answered with the current logo, not kept
        headers.CacheControl = v == Version(theme) ? "private, max-age=31536000, immutable" : "private, no-cache";
        headers.Vary = "Cookie";
        headers.XContentTypeOptions = "nosniff";

        // opened directly, an SVG logo must not run script on the app's origin
        headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        return Results.Bytes(bytes, contentType, entityTag: new EntityTagHeaderValue($"\"{Version(theme)}\""));
    }

    private static string Version(ThemeDto theme) => $"{theme.Id:N}-{theme.Revision}";
}
