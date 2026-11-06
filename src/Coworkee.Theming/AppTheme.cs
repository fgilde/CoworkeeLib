namespace Coworkee.Theming;

/// <summary>
/// A built-in theme (<paramref name="BasedOn"/>) with other palette colors, e.g. <c>["Primary"] = "#0a7d5a"</c>.
/// <paramref name="Dark"/> defaults to the light colors; a changed Primary or Secondary gets a readable contrast text unless one is given.
/// The other sections replace single values of the base theme's and keep the rest.
/// </summary>
public sealed record AppTheme(string Name, string BasedOn, IReadOnlyDictionary<string, string> Light, IReadOnlyDictionary<string, string>? Dark = null)
{
    /// <summary>The theme's options, the properties of CoworkeeTheme (and the app's theme class): <c>["NavBesideAppBar"] = true</c>.</summary>
    public IReadOnlyDictionary<string, object>? Options { get; init; }

    /// <summary>MudBlazor's layout properties: <c>["DefaultBorderRadius"] = "8px"</c>.</summary>
    public IReadOnlyDictionary<string, object>? Layout { get; init; }

    /// <summary>MudBlazor's typography by section: <c>["Default"] = new Dictionary&lt;string, object&gt; { ["FontSize"] = ".875rem" }</c>.</summary>
    public IReadOnlyDictionary<string, object>? Typography { get; init; }

    /// <summary>CSS for the whole app, after the theme; replaces the base theme's.</summary>
    public string? CustomCss { get; init; }
}
