using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Coworkee.Bff.Pwa;

/// <summary>
/// The installable app: the web manifest in the colours and with the logo of the visitor's organisation, the service worker, the icon and the
/// page the worker shows without network. A page turns them on by rendering <see cref="CoworkeePwa"/> into its head.
/// </summary>
internal static class PwaEndpoints
{
    public const string ManifestPath = "/manifest.webmanifest";

    public const string IconPath = "/bff/pwa/icon";

    public const string OfflinePath = "/bff/pwa/offline";

    public static void Map(WebApplication app)
    {
        app.MapGet(ManifestPath, ManifestAsync);
        app.MapGet(PwaServiceWorker.Path, PwaServiceWorker.Serve);
        app.MapGet(IconPath, IconAsync);
        app.MapGet(OfflinePath, OfflineAsync);
    }

    private static async Task<IResult> ManifestAsync(HttpContext context, LoadingScreenSource source, IOptions<BffOptions> options)
    {
        var theme = (await source.GetAsync(context, LoadingScreenPreferences.Read(context.Request).ThemeId))?.Theme;
        var pwa = options.Value.Pwa;
        var name = pwa.Name is { Length: > 0 } configured ? configured : context.Request.Host.Host;
        var (type, sizes) = Icon(theme, pwa.Icon);
        context.Response.Headers.CacheControl = "private, no-cache";
        context.Response.Headers.Vary = "Cookie";
        return Results.Json(new Dictionary<string, object?>
        {
            ["id"] = "/",
            ["name"] = name,
            ["short_name"] = name,
            ["start_url"] = "/",
            ["scope"] = "/",
            ["display"] = "standalone",
            ["background_color"] = LoadingScreenMarkup.PaletteColor(theme?.PaletteLight, "Background") ?? "#ffffff",
            ["theme_color"] = BarColor(theme?.PaletteLight) ?? "#594ae2",
            ["icons"] = new[] { new Dictionary<string, string?> { ["src"] = IconPath, ["type"] = type, ["sizes"] = sizes, ["purpose"] = "any" } },
        }, JsonSerializerOptions.Web, "application/manifest+json");
    }

    /// <summary>The colour of the app bar, which the browser's title bar should match.</summary>
    public static string? BarColor(JsonElement? palette) =>
        LoadingScreenMarkup.PaletteColor(palette, "AppbarBackground") ?? LoadingScreenMarkup.PaletteColor(palette, "Primary");

    // the theme's logo, else the app's icon
    private static async Task<IResult> IconAsync(HttpContext context, LoadingScreenSource source, IOptions<BffOptions> options)
    {
        var screen = await source.GetAsync(context, LoadingScreenPreferences.Read(context.Request).ThemeId);
        if (LoadingScreenLogo.Url(screen?.Theme) is not null)
        {
            return await LoadingScreenLogo.ServeAsync(context, source, null);
        }

        return options.Value.Pwa.Icon is { Length: > 1 } icon && icon[0] == '/' && icon[1] != '/' && icon[1] != '\\' ? Results.Redirect(icon) : Results.NotFound();
    }

    private static (string? Type, string Sizes) Icon(ThemeDto? theme, string? icon)
    {
        if (theme is not null && ThemeLogo.TryRead(theme.Logo, out var bytes, out var contentType))
        {
            // browsers pick installable icons by size; only PNG says it in its header
            return (contentType, contentType == "image/png" && bytes.Length > 24
                ? $"{BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16))}x{BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20))}"
                : "any");
        }

        var extension = Path.GetExtension(icon ?? string.Empty).ToLowerInvariant();
        return (extension switch { ".svg" => ThemeLogo.Svg, ".png" => "image/png", ".webp" => "image/webp", ".jpg" or ".jpeg" => "image/jpeg", _ => null }, "any");
    }

    /// <summary>Shown by the service worker for a page while there is no network: the organisation's colours and logo, a hint and a reload link.</summary>
    private static async Task<IResult> OfflineAsync(HttpContext context, LoadingScreenSource source, IOptions<BffOptions> options)
    {
        var preferences = LoadingScreenPreferences.Read(context.Request);
        var theme = (await source.GetAsync(context, preferences.ThemeId))?.Theme;
        var texts = await source.TextsAsync(context, preferences.Culture);
        var logo = theme is not null && ThemeLogo.TryRead(theme.Logo, out var bytes, out var contentType) ? $"data:{contentType};base64,{Convert.ToBase64String(bytes)}" : null;
        string Color(string name, string fallback) => LoadingScreenMarkup.PaletteColor(theme?.PaletteLight, name) ?? fallback;
        string Encode(string value) => WebUtility.HtmlEncode(value);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Content($$"""
            <!doctype html><html lang="{{Encode(texts.Culture)}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
            <title>{{Encode(options.Value.Pwa.Name ?? texts.Title)}}</title><style>
            body{margin:0;min-height:100dvh;display:grid;place-items:center;font-family:system-ui,sans-serif;background:{{Color("Background", "#ffffff")}};color:{{Color("TextPrimary", "#424242")}};padding:24px;box-sizing:border-box;text-align:center}
            main{max-width:420px}img{width:72px;height:72px;object-fit:contain}h1{font-size:1.3rem;margin:16px 0 8px}p{opacity:.8;line-height:1.5}
            a{display:inline-block;margin-top:12px;padding:12px 28px;border-radius:999px;background:{{Color("Primary", "#594ae2")}};color:{{Color("PrimaryContrastText", "#ffffff")}};text-decoration:none;font-weight:600}
            </style></head><body><main>{{(logo is null ? string.Empty : $"<img src=\"{Encode(logo)}\" alt=\"\">")}}
            <h1>{{Encode(texts.Title)}}</h1><p>{{Encode(texts.Message)}}</p><a href="">{{Encode(texts.Reload)}}</a></main></body></html>
            """, "text/html; charset=utf-8");
    }
}
