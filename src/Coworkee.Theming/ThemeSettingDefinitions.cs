using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Coworkee.Settings;

namespace Coworkee.Theming;

internal sealed class ThemeSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Appearance", "Appearance")
            .Add(ThemeSettings.Mode, "Color mode", SettingType.Choice, [SettingScope.Global, SettingScope.Tenant, SettingScope.User], "system", visibleToClient: true, choices: ["system", "light", "dark"])
            .Add(ThemeSettings.ThemeId, "Theme", SettingType.String, [SettingScope.User], visibleToClient: true);
}
