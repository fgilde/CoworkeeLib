using System.Globalization;
using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Settings;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    public const string LibreTranslateImage = "libretranslate/libretranslate";
    public const string LibreTranslateTag = "v1.9.6";

    private IResourceBuilder<ContainerResource>? _libreTranslate;

    /// <summary>LibreTranslate of <see cref="CoworkeeAppOptions.UseLibreTranslate"/>, added with the first service that has the localization module; development only, a deployment sets the endpoint in the settings.</summary>
    public IResourceBuilder<ContainerResource>? LibreTranslate => Options.LibreTranslateLanguages is { } languages && !Publishing ? _libreTranslate ??= AddLibreTranslate(languages) : null;

    /// <summary>The language codes LibreTranslate loads for <paramref name="cultures"/>: English and the language of each culture.</summary>
    public static IReadOnlyList<string> LibreTranslateCodes(IEnumerable<string> cultures) =>
        [.. new[] { "en" }.Concat(cultures.Select(c => c.Trim()).Where(c => c.Length > 0).Select(LibreTranslateCode)).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string LibreTranslateCode(string culture)
    {
        var info = CultureInfo.GetCultureInfo(culture);
        return info.TwoLetterISOLanguageName != "zh" ? info.TwoLetterISOLanguageName
            : culture.Contains("Hant", StringComparison.OrdinalIgnoreCase) || culture.EndsWith("TW", StringComparison.OrdinalIgnoreCase) || culture.EndsWith("HK", StringComparison.OrdinalIgnoreCase) ? "zh-Hant" : "zh-Hans";
    }

    // the first start downloads the models, later starts only the missing ones (a language added to the list); the services wait for them
    private IResourceBuilder<ContainerResource> AddLibreTranslate(IReadOnlyList<string> languages)
    {
        var server = Builder.AddContainer($"{Name}-translate", LibreTranslateImage, LibreTranslateTag)
            .WithHttpEndpoint(targetPort: 5000, name: "http")
            .WithEnvironment("LT_LOAD_ONLY", string.Join(',', LibreTranslateCodes([.. languages, Options.TranslationSource ?? "en"])))
            .WithEnvironment("LT_UPDATE_MODELS", "true")
            .WithHttpHealthCheck("/health")
            .WithUrlForEndpoint("http", u => u.DisplayText = "LibreTranslate");
        if (Persistent)
        {
            server.WithVolume("coworkee-libretranslate-models", "/home/libretranslate/.local");
        }

        Options.LibreTranslateServer?.Invoke(server);
        return server;
    }

    private void WireTranslation(IResourceBuilder<ProjectResource> service)
    {
        if (Options.TranslationSource is { Length: > 0 } source)
        {
            service.WithSetting(s => s.Coworkee.Settings.Defaults["Localization.TranslationSource"], source);
        }

        if (Options.TranslationProvider is not { } provider)
        {
            return;
        }

        service.WithSetting(s => s.Coworkee.Settings.Defaults["Localization.TranslationProvider"], provider);
        if (LibreTranslate is { } server)
        {
            service.WaitFor(server).WithSetting(s => s.Coworkee.Settings.Defaults["Localization.TranslationEndpoint"], server.GetEndpoint("http"));
        }

        var (key, region, email) = Options.TranslationParameters;
        foreach (var (setting, parameter) in new[] { ("Localization.TranslatorKey", key), ("Localization.TranslatorRegion", region), ("Localization.TranslationEmail", email) })
        {
            if (parameter is not null)
            {
                service.WithSetting(s => s.Coworkee.Settings.Defaults[setting], parameter);
            }
        }
    }
}
