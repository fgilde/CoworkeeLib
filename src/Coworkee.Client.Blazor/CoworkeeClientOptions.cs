namespace Coworkee.Client.Blazor;

public sealed class CoworkeeClientOptions
{
    public string AppTitle { get; set; } = "Coworkee";

    public string LoginPath { get; set; } = "/bff/login";

    public string LogoutPath { get; set; } = "/bff/logout";
}

public sealed record CoworkeeNavItem(string Title, string Href, string Icon, string? Permission = null);

public interface INavigationContributor
{
    IEnumerable<CoworkeeNavItem> Items { get; }
}
