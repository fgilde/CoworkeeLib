using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

public sealed partial class CoworkeeAppOptions
{
    internal string? TranslationProvider { get; private set; }

    internal (IResourceBuilder<ParameterResource>? Key, IResourceBuilder<ParameterResource>? Region, IResourceBuilder<ParameterResource>? Email) TranslationParameters { get; private set; }

    internal IReadOnlyList<string>? LibreTranslateLanguages { get; private set; }

    internal Action<IResourceBuilder<ContainerResource>>? LibreTranslateServer { get; private set; }

    /// <summary>The language machine translations start from (setting Localization.TranslationSource, default "en").</summary>
    public string? TranslationSource { get; set; }

    /// <summary>
    /// The translation service of the app (setting Localization.TranslationProvider): libreTranslate, myMemory, deepL, azure, google, ai or none,
    /// with key, Azure region and MyMemory contact as parameters (e.g. from the user secrets). Administrators change it later in the settings.
    /// </summary>
    public CoworkeeAppOptions UseTranslation(
        string provider, IResourceBuilder<ParameterResource>? key = null, IResourceBuilder<ParameterResource>? region = null, IResourceBuilder<ParameterResource>? email = null) =>
        Set(() => (TranslationProvider, TranslationParameters, LibreTranslateLanguages, LibreTranslateServer) = (provider, (key, region, email), null, null));

    /// <summary>
    /// LibreTranslate, a free open source translation server without an account, as the translation service of the app. It loads only the models of
    /// <paramref name="languages"/> and the source language (cultures like "fr-CH" count as "fr"; English is always there) and keeps them in a volume all apps on the machine share.
    /// </summary>
    public CoworkeeAppOptions UseLibreTranslate(IEnumerable<string> languages, Action<IResourceBuilder<ContainerResource>>? configure = null) =>
        Set(() => (TranslationProvider, TranslationParameters, LibreTranslateLanguages, LibreTranslateServer) =
            ("libreTranslate", default, [.. languages], (LibreTranslateServer ?? (_ => { })) + configure));

    public CoworkeeAppOptions WithoutLibreTranslate() =>
        Set(() => (TranslationProvider, LibreTranslateLanguages, LibreTranslateServer) = (LibreTranslateLanguages is null ? TranslationProvider : null, null, null));
}
