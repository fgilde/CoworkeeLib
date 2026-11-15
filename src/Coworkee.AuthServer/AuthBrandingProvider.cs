using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Application.Caching;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Security;
using Coworkee.Theming;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer;

public sealed partial class AuthBrandingProvider(
    IServiceProvider services, HybridCache cache, IOptionsMonitor<AuthServerOptions> options, Settings.ISettingProvider settings, Microsoft.AspNetCore.Http.IHttpContextAccessor http)
{
    private static readonly HybridCacheEntryOptions Entry = new() { Expiration = TimeSpan.FromMinutes(1), LocalCacheExpiration = TimeSpan.FromMinutes(1) };

    private static readonly (string Variable, string Palette)[] Colors =
    [
        ("--paper", "Background"), ("--surface", "Surface"), ("--ink", "TextPrimary"), ("--ink-2", "TextSecondary"), ("--rule", "LinesDefault"),
        ("--signal", "Primary"), ("--on-signal", "PrimaryContrastText"), ("--accent", "Secondary"), ("--tertiary", "Tertiary"), ("--error", "Error"), ("--success", "Success"),
    ];

    /// <summary>Cookie of the auth server with the theme the browser used last (<see cref="SignInDesign.ThemeParameter"/>).</summary>
    public const string ThemeCookie = "coworkee-theme";

    // the themes wait for the cache; the design follows the configuration at once, which picks up changes made in the app with its next reload.
    // The design is the one the theme the browser used last names (cookie, a theme of the system organisation), else the one of the default theme, else the default design.
    // The preview of the Sign-in page brings an unsaved design for its one request (already checked).
    public async ValueTask<AuthBranding> GetAsync(CancellationToken cancellationToken)
    {
        var configured = options.CurrentValue;
        var themes = await cache.GetOrCreateInScopeAsync(services, new ImpersonatedUser(null, null), "coworkee:auth-branding", LoadAsync, Entry, cancellationToken: cancellationToken);
        var theme = Pick(themes, ThemeHint(http.HttpContext));
        return new AuthBranding(configured.DisplayName, theme?.Logo ?? configured.LogoUrl, theme?.Css,
            SignInPreview.Design(http.HttpContext) ?? Checked(configured.Design.For(theme?.Design)));
    }

    /// <summary>"light", "dark" or "system": the color mode setting of the user, else of the installation.</summary>
    public async Task<string> ModeAsync(CancellationToken cancellationToken) =>
        await settings.GetAsync(ThemeSettings.Mode, cancellationToken) switch { "light" => "light", "dark" => "dark", _ => "system" };

    /// <summary>The theme of <paramref name="hint"/> when the visitor may use it, else the default theme.</summary>
    internal static AuthTheme? Pick(AuthTheme[] themes, Guid? hint) =>
        themes.FirstOrDefault(t => t.Id == hint) ?? themes.FirstOrDefault(t => t.IsDefault) ?? themes.FirstOrDefault();

    /// <summary>
    /// Keeps the theme an authorization request names (<see cref="SignInDesign.ThemeParameter"/>, sent by the web app) in <see cref="ThemeCookie"/>, so the
    /// account pages of this browser show its design; only a theme id, which picks among the themes the pages may show anyway.
    /// </summary>
    internal static void RememberTheme(Microsoft.AspNetCore.Http.HttpContext context, string? theme)
    {
        if (Guid.TryParse(theme, out var id) && ThemeHint(context) != id)
        {
            context.Response.Cookies.Append(ThemeCookie, id.ToString(), new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = true, Secure = context.Request.IsHttps, SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(365), IsEssential = true, Path = "/",
            });
        }
    }

    /// <summary>The theme in the auth server's cookie, written when the web app names it in the authorization request.</summary>
    internal static Guid? ThemeHint(Microsoft.AspNetCore.Http.HttpContext? context) =>
        context?.Request.Cookies.TryGetValue(ThemeCookie, out var value) == true && Guid.TryParse(value, out var id) ? id : null;

    // the same for every visitor: the themes of the system organisation an anonymous visitor may have used, one entry, so no theme id makes the cache grow
    private static async Task<AuthTheme[]> LoadAsync(IServiceProvider scope, CancellationToken cancellationToken)
    {
        var themes = await scope.GetRequiredService<IDispatcher>().SendAsync(new GetSignInThemes(), cancellationToken);
        return themes.IsSuccess
            ? [.. themes.Value.Select(t => new AuthTheme(t.Id, t.IsDefault, ThemeLogo.ToDataUrl(t.Logo), Css(t), SignInDesign.KeyOf(t.Options)))]
            : [];
    }

    internal static string Css(ThemeDto theme)
    {
        var dark = Declarations(PaletteColors(theme.PaletteDark));
        return $":root{{{Declarations(PaletteColors(theme.PaletteLight))}{Declarations(Shape(theme))}}}:root[data-mode=dark]{{{dark}}}"
            + $"@media (prefers-color-scheme: dark){{:root:not([data-mode=light]){{{dark}}}}}";
    }

    // the layout name becomes a view path, the media address part of the page and of the content security policy
    internal static SignInDesign Checked(SignInDesign design) => new()
    {
        Layout = design.Layout is { } layout && Name().IsMatch(layout) ? layout : null,
        MediaUrl = SafeUrl(design.MediaUrl),
        MediaSide = design.MediaSide,
        BackgroundUrl = SafeUrl(design.BackgroundUrl),
        BackgroundDim = design.BackgroundDim is { } dim ? Math.Clamp(dim, 0, 90) : null,
        // plain text, encoded where the pages show it
        ShowAppName = design.ShowAppName,
        Texts = design.Texts ?? new SignInTexts(),
    };

    // a path on the auth server or an http(s) address: nothing that runs script or leaves for another host without saying so
    private static string? SafeUrl(string? url) =>
        url is not null && ((url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.Contains('\\')) || Clients.ClientApp.IsWebAddress(url)) ? url : null;

    private static IEnumerable<(string Variable, string Value)> PaletteColors(JsonElement palette) => Colors
        .Select(c => (c.Variable, Value: palette.TryGetProperty(c.Palette, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null))
        .Where(c => c.Value is not null && Color().IsMatch(c.Value))
        .Select(c => (c.Variable, c.Value!));

    private static IEnumerable<(string Variable, string Value)> Shape(ThemeDto theme)
    {
        if (theme.LayoutProperties is { ValueKind: JsonValueKind.Object } layout && layout.TryGetProperty("DefaultBorderRadius", out var radius)
            && radius.ValueKind == JsonValueKind.String && Length().IsMatch(radius.GetString()!))
        {
            yield return ("--radius", radius.GetString()!);
        }

        if (theme.Typography is { ValueKind: JsonValueKind.Object } typography && typography.TryGetProperty("Default", out var body)
            && body.ValueKind == JsonValueKind.Object && body.TryGetProperty("FontFamily", out var families) && families.ValueKind == JsonValueKind.Array)
        {
            var fonts = families.EnumerateArray().Select(f => f.ValueKind == JsonValueKind.String ? f.GetString() : null)
                .OfType<string>().Where(f => FontName().IsMatch(f)).Select(f => f.Contains(' ') ? $"\"{f}\"" : f).ToList();
            if (fonts.Count > 0)
            {
                yield return ("--font", string.Join(", ", fonts));
            }
        }
    }

    private static string Declarations(IEnumerable<(string Variable, string Value)> values) => string.Concat(values.Select(v => $"{v.Variable}:{v.Value};"));

    [GeneratedRegex(@"^(#[0-9a-fA-F]{3,8}|rgba?\([0-9.,%\s]+\))$")]
    private static partial Regex Color();

    [GeneratedRegex(@"^\d{1,3}(\.\d+)?(px|rem|em|%)$")]
    private static partial Regex Length();

    [GeneratedRegex(@"^[A-Za-z0-9 \-]{1,60}$")]
    private static partial Regex FontName();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,40}$")]
    private static partial Regex Name();
}
