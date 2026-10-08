using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;

namespace Coworkee.Localization.Settings;

internal sealed class LocalizationSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Localization", "Localization")
            .Add(LocalizationSettings.Culture, "Language", SettingType.String, [SettingScope.Global, SettingScope.Tenant, SettingScope.User], visibleToClient: true)
            .Add(LocalizationSettings.TranslatorKey, "Translator key", SettingType.Secret, [SettingScope.Global],
                description: "Azure AI Translator key. With it, switching a language on translates the texts it lacks.")
            .Add(LocalizationSettings.TranslatorRegion, "Translator region", SettingType.String, [SettingScope.Global],
                description: "Region of the translator resource, e.g. germanywestcentral.");
}
