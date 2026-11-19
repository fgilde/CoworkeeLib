using Coworkee.Application.Authorization;
using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

internal sealed class SettingsPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(SettingsPermissions.GroupName, "Settings").Add(SettingsPermissions.Manage, "Manage settings")
            .Add(SettingsPermissions.ManageGlobalClientValues, "Manage the client values of all users");
}
