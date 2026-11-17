using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting;

internal sealed class ReportingNavigation(ReportingOptions options) : INavigationContributor
{
    public IEnumerable<CoworkeeNavItem> Items => [new("Reports", "/" + options.Path(), MudBlazor.Icons.Material.Outlined.SpaceDashboard, ReportPermissions.View, Order: -7)];
}
