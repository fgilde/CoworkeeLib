using Coworkee.Application.Authorization;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting;

internal sealed class ReportPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(ReportPermissions.GroupName, "Reports")
            .Add(ReportPermissions.View, "View the reports shared with you")
            .Add(ReportPermissions.Manage, "Create reports and edit your own", ReportPermissions.View)
            .Add(ReportPermissions.Share, "Share reports with roles or the organisation", ReportPermissions.Manage);
}
