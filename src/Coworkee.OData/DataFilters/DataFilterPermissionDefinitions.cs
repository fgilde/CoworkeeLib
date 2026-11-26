using Coworkee.Application.Authorization;
using Coworkee.Contracts.DataFilters;

namespace Coworkee.OData.DataFilters;

internal sealed class DataFilterPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(DataFilterPermissions.GroupName, "Data filters")
            .Add(DataFilterPermissions.ManageSystem, "Write the data filters of all organisations")
            .Add(DataFilterPermissions.ManageTenant, "Write the data filters of the organisation")
            .Add(DataFilterPermissions.ManageGroups, "Write the data filters of groups")
            .Add(DataFilterPermissions.ManageRoles, "Write the data filters of roles")
            .Add(DataFilterPermissions.Bypass, "See all rows: no data filter applies");
}
