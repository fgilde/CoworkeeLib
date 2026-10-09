namespace Coworkee.Client.Blazor;

public sealed class CoworkeeClientOptions
{
    public string AppTitle { get; set; } = "Coworkee";

    public string? AppDescription { get; set; }

    /// <summary>Address of the app's logo (for example an SVG in wwwroot); a theme with its own logo replaces it.</summary>
    public string? AppLogo { get; set; }

    /// <summary>Shown in the about dialog; defaults to the informational version of the app assembly.</summary>
    public string? AppVersion { get; set; }

    public List<AboutLink> AboutLinks { get; } = [];

    /// <summary>Shown on the empty assistant page; name tasks the tools of the app can do.</summary>
    public string AssistantHint { get; set; } = "Ask for something the application can do, for example to find or change records.";

    public string LoginPath { get; set; } = "/bff/login";

    public string LogoutPath { get; set; } = "/bff/logout";

    /// <summary>
    /// When false, every page of the layout sends visitors who are not signed in straight to the sign-in (no anonymous home page);
    /// the setup wizard stays reachable. The BFF and the API keep their own rules either way.
    /// </summary>
    public bool AllowAnonymous { get; set; } = true;

    /// <summary>The sign-in address that returns to <paramref name="relativePath"/> (relative to the base address).</summary>
    public string SignInHref(string relativePath) => $"{LoginPath}?returnUrl={Uri.EscapeDataString("/" + relativePath)}";
}

public sealed record AboutLink(string Title, string Url);

/// <summary>A link of the navigation; hidden without <paramref name="Permission"/> or while the bool <paramref name="Feature"/> is off.</summary>
public sealed record CoworkeeNavItem(string Title, string Href, string Icon, string? Permission = null, bool ForceLoad = false, string? Group = null, int Order = 0, string? Feature = null);

public interface INavigationContributor
{
    IEnumerable<CoworkeeNavItem> Items { get; }
}
