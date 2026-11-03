using Coworkee.Application.Authorization;
using Coworkee.Contracts.Data;

namespace Coworkee.OData.Transfer;

internal sealed class DataTransferPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(DataTransferPermissions.GroupName, "Import and export")
            .Add(DataTransferPermissions.Use, "Open the import and export page")
            .Add(DataTransferPermissions.ExportSchedules, "Schedule list exports and mail them to others");
}
