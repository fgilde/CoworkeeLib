using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Application.Caching;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Security;
using Coworkee.Theming;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer;

/// <summary>
/// Name, logo, colors, font, radius and design of the account pages: the app's display name, the default theme of the system organisation and the configured <see cref="AuthServerOptions.Design"/>;
/// without a theme logo the configured <see cref="AuthServerOptions.LogoUrl"/>. <see cref="Css"/> sets the light colors on :root, the dark ones
/// for html[data-mode=dark] and, unless the mode is light, for a dark system preference.
/// </summary>
public sealed record AuthBranding(string AppName, string? LogoUrl, string? Css, SignInDesign Design)
{
    private static readonly string[] VideoExtensions = [".mp4", ".webm", ".ogg", ".ogv", ".mov"];

    public string Layout => Design.Layout ?? SignInLayouts.Default;

    /// <summary>The Razor layout of <see cref="Layout"/>: _Design{Layout}.cshtml next to the pages, the default layout when there is no such view.</summary>
    public string LayoutView(IRazorViewEngine views, ActionContext context) =>
        views.FindPage(context, "_Design" + Layout).Page is not null ? "_Design" + Layout : "_Design" + SignInLayouts.Default;

    public bool MediaIsVideo => Design.MediaUrl is { } url && VideoExtensions.Any(e => url.Split('?', '#')[0].EndsWith(e, StringComparison.OrdinalIgnoreCase));
}

public sealed partial class AuthBrandingProvider(IServiceScopeFactory scopes, HybridCache cache, IOptionsMonitor<AuthServerOptions> options, Settings.ISettingProvider settings)
{
    private static readonly HybridCacheEntryOptions Entry = new() { Expiration = TimeSpan.FromMinutes(1), LocalCacheExpiration = TimeSpan.FromMinutes(1) };

    private static readonly (string Variable, string Palette)[] Colors =
    [
        ("--paper", "Background"), ("--surface", "Surface"), ("--ink", "TextPrimary"), ("--ink-2", "TextSecondary"), ("--rule", "LinesDefault"),
        ("--signal", "Primary"), ("--on-signal", "PrimaryContrastText"), ("--accent", "Secondary"), ("--error", "Error"), ("--success", "Success"),
    ];

    // the theme part waits for the cache; the design follows the configuration at once, which picks up changes made in the app with its next reload
    public async ValueTask<AuthBranding> GetAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateInScopeAsync(scopes, new ImpersonatedUser(null, null), "coworkee:auth-branding", LoadAsync, Entry, cancellationToken: cancellationToken)
            with { Design = Checked(options.CurrentValue.Design) };

    /// <summary>"light", "dark" or "system": the color mode setting of the user, else of the installation.</summary>
    public async Task<string> ModeAsync(CancellationToken cancellationToken) =>
        await settings.GetAsync(ThemeSettings.Mode, cancellationToken) switch { "light" => "light", "dark" => "dark", _ => "system" };

    // the same for every visitor: the theme as an anonymous visitor gets it
    private async Task<AuthBranding> LoadAsync(IServiceProvider scope, CancellationToken cancellationToken)
    {
        var configured = options.CurrentValue;
        var theme = await scope.GetRequiredService<IDispatcher>().SendAsync(new GetCurrentTheme(), cancellationToken);
        if (!theme.IsSuccess)
        {
            return new AuthBranding(configured.DisplayName, configured.LogoUrl, null, new SignInDesign());
        }

        var dto = theme.Value;
        var logo = ThemeLogo.ToDataUrl(dto.Logo) ?? configured.LogoUrl;
        return new AuthBranding(configured.DisplayName, logo, Css(dto), new SignInDesign());
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
        MediaUrl = design.MediaUrl is { } url && ((url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)) || Clients.ClientApp.IsWebAddress(url)) ? url : null,
        MediaSide = design.MediaSide,
    };

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
