using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Contracts.Theming;

namespace Coworkee.Bff;

/// <summary>
/// The loading screen as HTML with inline style (no flash of wrong colours; logo and script are their own, cacheable requests): the theme's
/// colours for the remembered colour mode or the system preference, the logo in a ring that follows Blazor's <c>--blazor-load-percentage</c>, the chosen
/// background and, hidden until the start fails, an error message with a reload button. The app removes it through <c>window.coworkeeLoadingScreen.hide()</c>.
/// </summary>
public static partial class LoadingScreenMarkup
{
    private static readonly string[] VideoExtensions = [".mp4", ".webm", ".ogg", ".ogv", ".mov"];

    private static readonly (string Variable, string Palette, string Light, string Dark)[] Colors =
    [
        ("--cwl-primary", "Primary", "#594ae2", "#776be7"), ("--cwl-secondary", "Secondary", "#ff4081", "#ff4081"),
        ("--cwl-tertiary", "Tertiary", "#1ec8a5", "#1ec8a5"), ("--cwl-bg", "Background", "#ffffff", "#1a1a27"), ("--cwl-ink", "TextPrimary", "#424242", "#ffffffb3"),
        ("--cwl-on-primary", "PrimaryContrastText", "#ffffff", "#ffffff"),
    ];

    public static string Render(LoadingScreenDto? screen, string? mode, string? fallbackLogo, LoadingScreenTexts? texts = null)
    {
        texts ??= LoadingScreenTexts.English;
        var theme = screen?.Theme;
        var media = screen?.Background == LoadingScreenBackground.SignInMedia ? SafeUrl(screen.MediaUrl) : null;
        var background = screen?.Background == LoadingScreenBackground.Plain ? "plain" : media is null ? "animated" : "media";
        var logo = LoadingScreenLogo.Url(theme) ?? SafeUrl(fallbackLogo);
        var html = new StringBuilder(8 * 1024);
        html.Append($"<div id=\"coworkee-loading\" class=\"cwl-indeterminate\" data-background=\"{background}\" role=\"progressbar\" aria-valuemin=\"0\" aria-valuemax=\"100\" aria-label=\"{Encode(texts.Loading)}\" lang=\"{Encode(texts.Culture)}\">");
        html.Append("<style>").Append(Variables(theme, mode)).Append(LoadingScreenStyles.Css).Append("</style>");
        if (background != "plain")
        {
            html.Append("<div class=\"cwl-layer\" aria-hidden=\"true\"><span class=\"cwl-blob cwl-b1\"></span><span class=\"cwl-blob cwl-b2\"></span><span class=\"cwl-blob cwl-b3\"></span></div>");
        }

        if (media is not null)
        {
            // a media address that fails leaves the animated background
            html.Append("<div class=\"cwl-layer\" aria-hidden=\"true\">");
            html.Append(IsVideo(media)
                ? $"<video class=\"cwl-media\" src=\"{Encode(media)}\" muted loop playsinline preload=\"auto\"></video>"
                : $"<img class=\"cwl-media\" src=\"{Encode(media)}\" alt=\"\">");
            html.Append("<div class=\"cwl-veil\"></div></div>");
        }

        html.Append("<div class=\"cwl-center\"><div class=\"cwl-ring\">");
        html.Append(logo is null ? "<span class=\"cwl-mark\"></span>" : $"<img class=\"cwl-logo\" src=\"{Encode(logo)}\" alt=\"\">");
        html.Append("</div><div class=\"cwl-text\" aria-hidden=\"true\"></div></div>");
        html.Append($"<div class=\"cwl-error\" dir=\"auto\"><div class=\"cwl-error-title\">{Encode(texts.Title)}</div><p class=\"cwl-error-text\">{Encode(texts.Message)}</p>");
        html.Append($"<button type=\"button\" class=\"cwl-reload\">{Encode(texts.Reload)}</button>");
        html.Append($"<details class=\"cwl-details\"><summary>{Encode(texts.Details)}</summary><pre class=\"cwl-detail\"></pre></details></div>");

        // Blazor shows the first #blazor-error-ui of the page when the start fails: this hidden one tells the script (an app's own one comes later)
        html.Append("<div id=\"blazor-error-ui\" class=\"cwl-blazor-error\"></div>");
        html.Append($"<script src=\"{LoadingScreenScript.Url}\"></script></div>");
        return html.ToString();
    }

    private static string Variables(ThemeDto? theme, string? mode)
    {
        var light = Declarations(theme?.PaletteLight, dark: false) + "color-scheme:light;";
        var dark = Declarations(theme?.PaletteDark, dark: true) + "color-scheme:dark;";
        return mode switch
        {
            "dark" => $"#coworkee-loading{{{dark}}}",
            "light" => $"#coworkee-loading{{{light}}}",
            _ => $"#coworkee-loading{{{light}}}@media (prefers-color-scheme: dark){{#coworkee-loading{{{dark}}}}}",
        };
    }

    private static string Declarations(JsonElement? palette, bool dark) => string.Concat(Colors.Select(c =>
        $"{c.Variable}:{(palette is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(c.Palette, out var value) && value.ValueKind == JsonValueKind.String
            && Color().IsMatch(value.GetString()!) ? value.GetString() : dark ? c.Dark : c.Light)};"));

    // a path of this site or a web address, nothing that could run script or leave for another host unnoticed
    private static string? SafeUrl(string? url) =>
        url is { Length: > 0 } && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
        && (url[0] == '/' || !url.Contains(':') || (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "https" or "http"))
            ? url
            : null;

    private static bool IsVideo(string url) => VideoExtensions.Any(e => url.Split('?', '#')[0].EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    [GeneratedRegex(@"^(#[0-9a-fA-F]{3,8}|rgba?\([0-9.,%\s]+\))$")]
    private static partial Regex Color();
}
