using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;

namespace Coworkee.Localization.Settings;

internal sealed class LocalizationSettingDefinitions : ISettingDefinitionContributor
{
    private static readonly SettingScope[] Global = [SettingScope.Global];

    private static SettingCondition Provider(params string[] providers) => new(LocalizationSettings.TranslationProvider, providers);

    public void Define(SettingDefinitionContext context)
    {
        context.Group("Localization", "Localization")
            .Add(LocalizationSettings.Culture, "Default language", SettingType.Language, [SettingScope.Global, SettingScope.Tenant, SettingScope.User], visibleToClient: true,
                description: "The language the app starts in. An organisation overrides the system, a user overrides both; empty inherits.");
        context.Group("Translation", "Translation")
            .Add(LocalizationSettings.TranslationProvider, "Translation service", SettingType.Choice, Global, TranslationProviders.Azure, choices: TranslationProviders.All,
                description: "Translates the texts a language lacks. libreTranslate: free, self-hosted, no account. myMemory: free public service, 5,000 characters a day (50,000 with an email). deepL, azure, google: an API key. ai: the model of the AI assistant.")
            .Add(LocalizationSettings.TranslatorKey, "Translation API key", SettingType.Secret, Global,
                description: "Key of the translation service (Azure, DeepL, Google; optional for LibreTranslate).", enabledWhen: Provider(TranslationProviders.DeepL, TranslationProviders.Azure, TranslationProviders.Google, TranslationProviders.LibreTranslate))
            .Add(LocalizationSettings.TranslatorRegion, "Azure region", SettingType.String, Global,
                description: "Region of the Azure translator resource, e.g. germanywestcentral.", enabledWhen: Provider(TranslationProviders.Azure))
            .Add(LocalizationSettings.TranslationEndpoint, "Translation endpoint", SettingType.String, Global,
                description: "Address of the LibreTranslate server, e.g. http://localhost:5000. Optional for the other services.", enabledWhen: Provider(TranslationProviders.LibreTranslate, TranslationProviders.DeepL, TranslationProviders.Azure, TranslationProviders.Google))
            .Add(LocalizationSettings.TranslationEmail, "Contact email (MyMemory)", SettingType.String, Global,
                description: "Raises the free MyMemory quota to 50,000 characters a day.", enabledWhen: Provider(TranslationProviders.MyMemory))
            .Add(LocalizationSettings.TranslationSource, "Source language", SettingType.String, Global, "en",
                description: "The language translations start from, e.g. de; texts it lacks start from English.")
            .Add(LocalizationSettings.AutoTranslate, "Translate when a language is switched on", SettingType.Bool, Global, "true");
    }
}
