namespace Coworkee.Client.Blazor.Navigation;

public sealed class NavigationMenuOptions
{
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string? Group, string? Title, int? Order)> _placements = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _groupOrder = new(StringComparer.Ordinal)
    {
        [NavigationGroups.Identity] = 0,
        [NavigationGroups.System] = 1,
        [NavigationGroups.Communication] = 2,
        [NavigationGroups.Localization] = 3,
        [NavigationGroups.Monitoring] = 4,
    };
    private readonly Dictionary<string, string> _groupIcons = new(StringComparer.Ordinal) 
    {
        [NavigationGroups.Administration] = MudBlazor.Icons.Material.Outlined.AdminPanelSettings,
        [NavigationGroups.Identity] = MudBlazor.Icons.Material.Outlined.Badge,
        [NavigationGroups.System] = MudBlazor.Icons.Material.Outlined.SettingsApplications,
        [NavigationGroups.Communication] = MudBlazor.Icons.Material.Outlined.Forum,
        [NavigationGroups.Localization] = MudBlazor.Icons.Material.Outlined.Translate,
        [NavigationGroups.Monitoring] = MudBlazor.Icons.Material.Outlined.MonitorHeart,
    };

    public bool ShowHome { get; set; } = true;

    public string HomeTitle { get; set; } = "Home";

    public IReadOnlySet<string> Hidden => _hidden;

    public NavigationMenuOptions Hide(params string[] hrefs)
    {
        _hidden.UnionWith(hrefs);
        return this;
    }

    /// <summary>Moves a link into another group, a path like "Administration/Localization" nests it.</summary>
    public NavigationMenuOptions Place(string href, string? group, string? title = null, int? order = null)
    {
        _placements[href] = (group, title, order);
        return this;
    }

    public CoworkeeNavItem Arrange(CoworkeeNavItem item) =>
        _placements.TryGetValue(item.Href, out var place)
            ? item with { Group = place.Group, Title = place.Title ?? item.Title, Order = place.Order ?? item.Order }
            : item;

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
