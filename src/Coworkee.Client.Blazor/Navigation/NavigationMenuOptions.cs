namespace Coworkee.Client.Blazor.Navigation;

public sealed class NavigationMenuOptions
{
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _groupOrder = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _groupIcons = new(StringComparer.Ordinal) { [NavigationGroups.Administration] = MudBlazor.Icons.Material.Outlined.AdminPanelSettings };

    public bool ShowHome { get; set; } = true;

    public string HomeTitle { get; set; } = "Home";

    public IReadOnlySet<string> Hidden => _hidden;

    public NavigationMenuOptions Hide(params string[] hrefs)
    {
        _hidden.UnionWith(hrefs);
        return this;
    }

    public NavigationMenuOptions OrderGroup(string group, int order)
    {
        _groupOrder[group] = order;
        return this;
    }

    public NavigationMenuOptions IconForGroup(string group, string icon)
    {
        _groupIcons[group] = icon;
        return this;
    }

    public int GroupOrder(string group) => _groupOrder.GetValueOrDefault(group, 1000);

    public string GroupIcon(string group) => _groupIcons.GetValueOrDefault(group, MudBlazor.Icons.Material.Outlined.Folder);
}
