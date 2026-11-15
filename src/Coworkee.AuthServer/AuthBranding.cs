using System.Globalization;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;

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

    /// <summary>What <see cref="Layout"/> shows (<see cref="SignInLayoutOptions.For"/>); the pages leave out what it does not.</summary>
    public SignInLayoutOptions Options => SignInLayoutOptions.For(Layout);

    /// <summary>
    /// The Razor layout of <see cref="Layout"/>: _Design{Layout}.cshtml next to the pages; the card for the former Glass and Aurora (unless the app has a view
    /// of that name), else the default layout when there is no such view.
    /// </summary>
    public string LayoutView(IRazorViewEngine views, ActionContext context) =>
        views.FindPage(context, "_Design" + Layout).Page is not null ? "_Design" + Layout
        : SignInLayouts.IsFormer(Layout) ? "_Design" + SignInLayouts.Card
        : "_Design" + SignInLayouts.Default;

    public bool MediaIsVideo => IsVideo(Design.MediaUrl);

    public bool BackgroundIsVideo => IsVideo(Design.BackgroundUrl);

    /// <summary>Background 1 shows: a file or a fill.</summary>
    public bool HasBackground => Design.BackgroundUrl is not null || PageFill is not null;

    /// <summary>CSS of background 1 as fill, null with a file or without a fill.</summary>
    public string? PageFill => Design.BackgroundUrl is null ? SignInFills.Css(Design.PageFill, ThemeColor) : null;

    /// <summary>CSS of background 2 as fill, null with a file or without a fill.</summary>
    public string? PanelFill => Design.MediaUrl is null && Options.PanelBackground is not null ? SignInFills.Css(Design.PanelFill, ThemeColor) : null;

    public bool GlassSurface => Options.Surface && Design.Surface == SignInSurface.Glass;

    public bool Aurora => Options.Animation && Design.Animation == SignInAnimation.Aurora;

    /// <summary>The fills, tilt and aurora colors as CSS variables on &lt;body&gt;; built from checked values only.</summary>
    public string? Style
    {
        get
        {
            var style = new List<string>();
            if (PageFill is { } page)
            {
                style.Add($"--page-fill:{page}");
            }

            if (PanelFill is { } panel)
            {
                style.Add($"--panel-fill:{panel}");
            }

            if (Options.Tilt)
            {
                style.Add($"--tilt:{(Design.Tilt ?? SignInLayoutOptions.DefaultTilt).ToString(CultureInfo.InvariantCulture)}deg");
            }

            if (Aurora && Design.BackgroundUrl is null && SignInFills.ColorsOf(Design.PageFill, ThemeColor) is { Count: > 0 } colors)
            {
                // the aurora drifts in the fill's colors
                style.AddRange(Enumerable.Range(0, 3).Select(i => $"--aurora-{i + 1}:{colors[(i * Math.Max(colors.Count - 1, 1) / 2) % colors.Count]}"));
            }

            return style.Count == 0 ? null : string.Join(';', style);
        }
    }

    /// <summary>The classes of &lt;body&gt; besides the layout's: background, animated fills, surface.</summary>
    public string Classes => string.Join(' ', new[]
    {
        HasBackground ? "has-background" : null,
        Design.BackgroundUrl is not null ? "has-image" : null,
        SignInFills.IsAnimated(Design.PageFill) && PageFill is not null ? "page-fill-animated" : null,
        SignInFills.IsAnimated(Design.PanelFill) && PanelFill is not null ? "panel-fill-animated" : null,
        // text on a fill follows its brightness
        PageFill is null ? null : SignInFills.IsDark(Design.PageFill) switch { true => "page-dark", false => "page-light", _ => null },
        PanelFill is null ? null : SignInFills.IsDark(Design.PanelFill) switch { true => "panel-dark", false => "panel-light", _ => null },
        GlassSurface ? "surface-glass" : null,
        Aurora ? "with-aurora" : null,
    }.OfType<string>());

    /// <summary>The darkening of background 1 in percent, 0 for layouts without it.</summary>
    public int Dim => Options.Dim ? Design.BackgroundDim ?? 0 : 0;

    /// <summary>The theme colors a fill may name, as the variables of these pages.</summary>
    public static string ThemeColor(string color) => color switch
    {
        "primary" => "var(--signal)",
        "secondary" => "var(--accent)",
        "tertiary" => "var(--tertiary)",
        "background" => "var(--paper)",
        _ => "var(--surface)",
    };

    private static bool IsVideo(string? url) => url is not null && VideoExtensions.Any(e => url.Split('?', '#')[0].EndsWith(e, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A theme the sign-in page may show: its logo as data URL, its colors as <see cref="AuthBranding.Css"/> and the key of the sign-in design it names.</summary>
public sealed record AuthTheme(Guid Id, bool IsDefault, string? Logo, string? Css, string? Design);
