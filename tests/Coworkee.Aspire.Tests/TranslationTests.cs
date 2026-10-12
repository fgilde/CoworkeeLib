using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Coworkee.Aspire.Tests;

public sealed class TranslationTests
{
    [Fact]
    public async Task LibreTranslate_loads_only_the_app_languages_and_becomes_the_translation_service_of_the_api()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => { o.TranslationSource = "it"; o.UseLibreTranslate(["de", "fr-CH", "zh-TW", "fr"]); });

        app.AddProjects();

        var server = app.LibreTranslate!.Resource;
        server.Annotations.OfType<ContainerImageAnnotation>().Single().Image.ShouldBe(CoworkeeApp.LibreTranslateImage);
        var environment = await server.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
        environment["LT_LOAD_ONLY"].ShouldBe("en,de,fr,zh-Hant,it");
        environment["LT_UPDATE_MODELS"].ShouldBe("true");
        server.Annotations.OfType<ContainerMountAnnotation>().Single().Target.ShouldBe("/home/libretranslate/.local");
        var api = await app.Apis[0].Resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
        api["Coworkee__Settings__Defaults__Localization.TranslationProvider"].ShouldBe("libreTranslate");
        api["Coworkee__Settings__Defaults__Localization.TranslationEndpoint"].ShouldBe("{demo-translate.bindings.http.url}");
        api["Coworkee__Settings__Defaults__Localization.TranslationSource"].ShouldBe("it");
        app.Apis[0].Resource.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource).ShouldContain(server);
    }

    [Fact]
    public async Task Another_service_gets_its_key_and_region_from_parameters_and_no_container()
    {
        var builder = DistributedApplication.CreateBuilder();
        var key = builder.AddParameter("translation-key", "secret", secret: true);
        var region = builder.AddParameter("translation-region", "westeurope");
        var app = builder.AddCoworkeeApp("demo", o => o.UseLibreTranslate(["fr"]).UseTranslation("azure", key, region));

        app.AddProjects();

        app.LibreTranslate.ShouldBeNull();
        var api = await app.Apis[0].Resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
        api["Coworkee__Settings__Defaults__Localization.TranslationProvider"].ShouldBe("azure");
        api["Coworkee__Settings__Defaults__Localization.TranslatorKey"].ShouldBe("{translation-key.value}");
        api["Coworkee__Settings__Defaults__Localization.TranslatorRegion"].ShouldBe("{translation-region.value}");
        api.Keys.ShouldNotContain("Coworkee__Settings__Defaults__Localization.TranslationEndpoint");
    }

    [Fact]
    public void Without_it_there_is_no_translation_server()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseLibreTranslate(["fr"]).WithoutLibreTranslate());

        app.AddProjects();

        app.LibreTranslate.ShouldBeNull();
        builder.Resources.Select(r => r.Name).ShouldNotContain("demo-translate");
    }
}
