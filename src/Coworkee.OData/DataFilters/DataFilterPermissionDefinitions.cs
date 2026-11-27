using Coworkee.Application.Authorization;
using Coworkee.Contracts.DataFilters;

namespace Coworkee.OData.DataFilters;

internal sealed class DataFilterPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(DataFilterPermissions.GroupName, "Data filters")
            .Add(DataFilterPermissions.ManageSystem, "Write the data filters of all tenants")
            .Add(DataFilterPermissions.ManageTenant, "Write the data filters of the tenant")
            .Add(DataFilterPermissions.ManageGroups, "Write the data filters of groups")
            .Add(DataFilterPermissions.ManageRoles, "Write the data filters of roles")
            .Add(DataFilterPermissions.Bypass, "See all rows: no data filter of the own tenant applies (System filters neither, in the system tenant)");
}
