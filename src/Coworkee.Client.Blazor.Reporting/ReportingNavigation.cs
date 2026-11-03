using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting;

internal sealed class ReportingNavigation : INavigationContributor
{
    public IEnumerable<CoworkeeNavItem> Items => [new("Reports", "/reports", MudBlazor.Icons.Material.Outlined.SpaceDashboard, ReportPermissions.View, Order: -7)];
}
