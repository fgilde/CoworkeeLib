using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;

namespace Coworkee.Localization.Settings;

internal sealed class LocalizationSettingDefinitions : ISettingDefinitionContributor
{
    private static readonly SettingScope[] Global = [SettingScope.Global];

    public void Define(SettingDefinitionContext context)
    {
        context.Group("Localization", "Localization")
            .Add(LocalizationSettings.Culture, "Language", SettingType.String, [SettingScope.Global, SettingScope.Tenant, SettingScope.User], visibleToClient: true);
        context.Group("Translation", "Translation")
            .Add(LocalizationSettings.TranslationProvider, "Translation service", SettingType.Choice, Global, TranslationProviders.Azure, choices: TranslationProviders.All,
                description: "Translates the texts a language lacks. libreTranslate: free, self-hosted, no account. myMemory: free public service, 5,000 characters a day (50,000 with an email). deepL, azure, google: an API key. ai: the model of the AI assistant.")
            .Add(LocalizationSettings.TranslatorKey, "Translation API key", SettingType.Secret, Global,
                description: "Key of the translation service (Azure, DeepL, Google; optional for LibreTranslate).")
            .Add(LocalizationSettings.TranslatorRegion, "Azure region", SettingType.String, Global,
                description: "Region of the Azure translator resource, e.g. germanywestcentral.")
            .Add(LocalizationSettings.TranslationEndpoint, "Translation endpoint", SettingType.String, Global,
                description: "Address of the LibreTranslate server, e.g. http://localhost:5000. Optional for the other services.")
            .Add(LocalizationSettings.TranslationEmail, "Contact email (MyMemory)", SettingType.String, Global,
                description: "Raises the free MyMemory quota to 50,000 characters a day.")
            .Add(LocalizationSettings.TranslationSource, "Source language", SettingType.String, Global, "en",
                description: "The language translations start from, e.g. de; texts it lacks start from English.")
            .Add(LocalizationSettings.AutoTranslate, "Translate when a language is switched on", SettingType.Bool, Global, "true");
    }
}
