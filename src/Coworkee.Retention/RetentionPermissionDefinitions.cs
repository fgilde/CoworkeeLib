using Coworkee.Application.Authorization;
using Coworkee.Contracts.Retention;

namespace Coworkee.Retention;

internal sealed class RetentionPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(RetentionPermissions.GroupName, "Retention").Add(RetentionPermissions.Manage, "Set how long data is kept and delete old data");
}
