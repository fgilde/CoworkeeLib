using Coworkee.Application.Authorization;
using Coworkee.Contracts.Localization;

namespace Coworkee.Localization.Permissions;

internal sealed class LocalizationPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(LocalizationPermissions.GroupName, "Localization").Add(LocalizationPermissions.Manage, "Manage languages and translations");
}
