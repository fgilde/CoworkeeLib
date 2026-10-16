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

    /// <summary>Sections and credits of the about dialog.</summary>
    public Components.About.AboutOptions About { get; } = new();

    /// <summary>Defaults of the data tables, e.g. which column filters each type offers.</summary>
    public Components.Data.DataTableOptions DataTable { get; } = new();

    /// <summary>The generated proxies of application services: defaults for all and settings per service.</summary>
    public Coworkee.Client.Blazor.ServiceProxies.ServiceProxyOptions ServiceProxies { get; } = new();

    /// <summary>Shown in the empty assistant window; name tasks the tools of the app can do.</summary>
    public string AssistantHint { get; set; } = "Ask for something the application can do, for example to find or change records.";

    /// <summary>
    /// Pages of what assistant tools created or changed, by tool name, e.g. ["add_edit_product"] = "/catalog/products/{id}"; {id} comes from
    /// the tool's result (an id, or an object with an id). The tool chips in the assistant window link there.
    /// </summary>
    public Dictionary<string, string> AssistantLinks { get; } = new(StringComparer.Ordinal);

    /// <summary>Shows the sign-in page card (layout, image or video) on the Themes page; the API accepts changes while Coworkee:Auth:AllowDesignChanges is on.</summary>
    public bool AllowSignInDesign { get; set; } = true;

    /// <summary>Layouts offered on the Themes page; add the names of the auth server's own _Design{Name}.cshtml layouts.</summary>
    public List<string> SignInLayouts { get; } = [.. Contracts.Theming.SignInLayouts.All];

    /// <summary>
    /// Where the library's admin pages live: "/cw" serves /admin/users at /cw/admin/users, so the app keeps /admin for its own pages.
    /// Empty keeps the old addresses. Old addresses (bookmarks, links in mails) go on to the current ones.
    /// </summary>
    public string PagePrefix { get; set; } = "/cw";

    /// <summary>Replaces or turns off pages of the library by key, e.g. Pages.Replace&lt;MyUsers&gt;("users").Disable("backups").</summary>
    public Navigation.PageOptions Pages { get; } = new();

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
/// <summary>A navigation entry; <paramref name="HostOnly"/> ones show only to users of the system organisation.</summary>
public sealed record CoworkeeNavItem(
    string Title, string Href, string Icon, string? Permission = null, bool ForceLoad = false, string? Group = null, int Order = 0, string? Feature = null, bool HostOnly = false);

public interface INavigationContributor
{
    IEnumerable<CoworkeeNavItem> Items { get; }
}
