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

    /// <summary>The Razor layout of <see cref="Layout"/>: _Design{Layout}.cshtml next to the pages, the default layout when there is no such view.</summary>
    public string LayoutView(IRazorViewEngine views, ActionContext context) =>
        views.FindPage(context, "_Design" + Layout).Page is not null ? "_Design" + Layout : "_Design" + SignInLayouts.Default;

    public bool MediaIsVideo => IsVideo(Design.MediaUrl);

    public bool BackgroundIsVideo => IsVideo(Design.BackgroundUrl);

    private static bool IsVideo(string? url) => url is not null && VideoExtensions.Any(e => url.Split('?', '#')[0].EndsWith(e, StringComparison.OrdinalIgnoreCase));
}
