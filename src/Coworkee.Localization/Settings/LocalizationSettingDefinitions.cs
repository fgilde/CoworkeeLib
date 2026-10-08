using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;

namespace Coworkee.Localization.Settings;

internal sealed class LocalizationSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Localization", "Localization")
            .Add(LocalizationSettings.Culture, "Language", SettingType.String, [SettingScope.Global, SettingScope.Tenant, SettingScope.User], visibleToClient: true);
}
