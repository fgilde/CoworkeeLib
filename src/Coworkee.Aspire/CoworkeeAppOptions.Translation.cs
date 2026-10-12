using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

public sealed partial class CoworkeeAppOptions
{
    internal IReadOnlyList<string>? LibreTranslateLanguages { get; private set; }

    internal Action<IResourceBuilder<ContainerResource>>? LibreTranslateServer { get; private set; }

    /// <summary>
    /// LibreTranslate, a free open source translation server without an account, as the translation service of the app. It loads only the models of
    /// <paramref name="languages"/> (cultures like "fr-CH" count as "fr"; English is always there) and keeps them in a volume all apps on the machine share.
    /// </summary>
    public CoworkeeAppOptions UseLibreTranslate(IEnumerable<string> languages, Action<IResourceBuilder<ContainerResource>>? configure = null) =>
        Set(() => (LibreTranslateLanguages, LibreTranslateServer) = ([.. languages], (LibreTranslateServer ?? (_ => { })) + configure));

    public CoworkeeAppOptions WithoutLibreTranslate() => Set(() => (LibreTranslateLanguages, LibreTranslateServer) = (null, null));
}
