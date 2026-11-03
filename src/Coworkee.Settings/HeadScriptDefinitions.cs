using Coworkee.Application.Authorization;
using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

internal sealed class HeadScriptDefinitions(bool perOrganisation) : ISettingDefinitionContributor, IPermissionDefinitionContributor
{
    public void Define(SettingDefinitionContext context)
    {
        SettingScope[] scopes = perOrganisation ? [SettingScope.Global, SettingScope.Tenant] : [SettingScope.Global];
        context.Group("HeadScript", "Head script")
            .Add(HeadScriptSettings.Enabled, "Head script on", SettingType.Bool, scopes, "false", visibleToClient: true,
                description: "The app adds the head script to its page.", permission: SettingsPermissions.HeadScript)
            .Add(HeadScriptSettings.Value, "Head script", SettingType.Text, scopes, string.Empty, visibleToClient: true,
                description: "HTML with script tags, e.g. a tag manager. It runs in every user's browser.", permission: SettingsPermissions.HeadScript);
    }

    public void Define(PermissionDefinitionContext context) =>
        context.Group(SettingsPermissions.GroupName, "Settings").Add(SettingsPermissions.HeadScript, "Change the head script");
}
