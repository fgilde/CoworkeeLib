using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>An icon for a service by its name ("myapp-api", "mail", "pgadmin").</summary>
public static class ServiceIcons
{
    private static readonly (string Part, string Icon)[] Known =
    [
        ("dashboard", Icons.Material.Outlined.Dashboard),
        ("jobs", Icons.Material.Outlined.Schedule),
        ("auth", Icons.Material.Outlined.Key),
        ("keycloak", Icons.Material.Outlined.Key),
        ("api", Icons.Material.Outlined.Api),
        ("web", Icons.Material.Outlined.Web),
        ("mail", Icons.Material.Outlined.Email),
        ("redis", Icons.Material.Outlined.Memory),
        ("pgadmin", Icons.Material.Outlined.Storage),
        ("postgres", Icons.Material.Outlined.Storage),
        ("elastic", Icons.Material.Outlined.ManageSearch),
        ("kibana", Icons.Material.Outlined.ManageSearch),
        ("worker", Icons.Material.Outlined.Engineering),
    ];

    public static string For(string name) =>
        Known.FirstOrDefault(k => name.Contains(k.Part, StringComparison.OrdinalIgnoreCase)).Icon ?? Icons.Material.Outlined.Cloud;
}
