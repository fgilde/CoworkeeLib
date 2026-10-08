using Coworkee.Application.Authorization;
using Coworkee.Contracts.Backup;

namespace Coworkee.Backup;

internal sealed class BackupPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(BackupPermissions.GroupName, "Backups").Add(BackupPermissions.Manage, "Create, download and restore database backups");
}
