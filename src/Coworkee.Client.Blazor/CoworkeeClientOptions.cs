namespace Coworkee.Client.Blazor;

public sealed class CoworkeeClientOptions
{
    public string AppTitle { get; set; } = "Coworkee";

    public string? AppDescription { get; set; }

    /// <summary>Shown in the about dialog; defaults to the informational version of the app assembly.</summary>
    public string? AppVersion { get; set; }

    public List<AboutLink> AboutLinks { get; } = [];

    public string LoginPath { get; set; } = "/bff/login";

    public string LogoutPath { get; set; } = "/bff/logout";
}

public sealed record AboutLink(string Title, string Url);

public sealed record CoworkeeNavItem(string Title, string Href, string Icon, string? Permission = null, bool ForceLoad = false, string? Group = null, int Order = 0);

public interface INavigationContributor
{
    IEnumerable<CoworkeeNavItem> Items { get; }
}
