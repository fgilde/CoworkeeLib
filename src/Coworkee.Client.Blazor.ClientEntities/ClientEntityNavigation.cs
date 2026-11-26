namespace Coworkee.Client.Blazor.ClientEntities;

internal sealed class ClientEntityNavigation : INavigationContributor
{
    public IEnumerable<CoworkeeNavItem> Items =>
    [
        new("Data types", "/admin/local-copies", MudBlazor.Icons.Material.Outlined.OfflineBolt, Coworkee.Client.Blazor.Security.PermissionPolicy.AnyOf(Coworkee.Contracts.Settings.SettingsPermissions.Manage, Coworkee.Contracts.DataFilters.DataFilterPermissions.ManageSystem, Coworkee.Contracts.DataFilters.DataFilterPermissions.ManageTenant),
            Group: Navigation.NavigationGroups.System, Order: 4, HostOnly: true,
            Description: "Per entity set: local copy in the browsers (off, memory or SQLite) with row limits and sync, and the data filters that decide which rows who sees."),
    ];
}
