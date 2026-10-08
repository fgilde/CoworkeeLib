namespace Coworkee.Contracts.Localization;

public static class LocalizationSettings
{
    /// <summary>Culture of the user (or the tenant default), e.g. "de".</summary>
    public const string Culture = "Localization.Culture";

    /// <summary>Key of the Azure AI Translator; with it, enabling a language translates the texts it lacks.</summary>
    public const string TranslatorKey = "Localization.TranslatorKey";

    /// <summary>Region of the translator resource, e.g. "germanywestcentral".</summary>
    public const string TranslatorRegion = "Localization.TranslatorRegion";
}
