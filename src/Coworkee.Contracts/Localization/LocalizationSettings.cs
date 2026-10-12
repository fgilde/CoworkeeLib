namespace Coworkee.Contracts.Localization;

public static class LocalizationSettings
{
    /// <summary>Culture of the user (or the tenant default), e.g. "de".</summary>
    public const string Culture = "Localization.Culture";

    /// <summary>The machine translation service, one of <see cref="TranslationProviders"/>.</summary>
    public const string TranslationProvider = "Localization.TranslationProvider";

    /// <summary>Key of the translation service (Azure, DeepL, Google; optional for LibreTranslate).</summary>
    public const string TranslatorKey = "Localization.TranslatorKey";

    /// <summary>Region of the Azure translator resource, e.g. "germanywestcentral".</summary>
    public const string TranslatorRegion = "Localization.TranslatorRegion";

    /// <summary>Address of the service: required for LibreTranslate, optional for the others (e.g. a DeepL proxy).</summary>
    public const string TranslationEndpoint = "Localization.TranslationEndpoint";

    /// <summary>Contact address for MyMemory; raises its free quota from 5,000 to 50,000 characters a day.</summary>
    public const string TranslationEmail = "Localization.TranslationEmail";

    /// <summary>The language translations start from (default "en"); texts it lacks start from English.</summary>
    public const string TranslationSource = "Localization.TranslationSource";

    /// <summary>Whether switching a language on translates the texts it lacks (default true).</summary>
    public const string AutoTranslate = "Localization.AutoTranslate";
}

public static class TranslationProviders
{
    public const string None = "none";
    public const string LibreTranslate = "libreTranslate";
    public const string MyMemory = "myMemory";
    public const string DeepL = "deepL";
    public const string Azure = "azure";
    public const string Google = "google";
    public const string Ai = "ai";

    public static readonly string[] All = [None, LibreTranslate, MyMemory, DeepL, Azure, Google, Ai];
}
