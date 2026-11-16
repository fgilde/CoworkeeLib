namespace Coworkee.Client.Blazor.ClientEntities;

internal sealed class ClientEntityNavigation : INavigationContributor
{
    public IEnumerable<CoworkeeNavItem> Items =>
    [
        new("Local copies", "/admin/local-copies", MudBlazor.Icons.Material.Outlined.OfflineBolt, Coworkee.Contracts.Settings.SettingsPermissions.Manage,
            Group: Navigation.NavigationGroups.System, Order: 4, HostOnly: true,
            Description: "Which data the browsers keep a copy of (off, memory or SQLite), with row limits and sync, per entity set."),
    ];
}
