using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Coworkee.Aspire.Tests;

public sealed class TranslationTests
{
    [Fact]
    public async Task LibreTranslate_loads_only_the_app_languages_and_becomes_the_translation_service_of_the_api()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseLibreTranslate(["de", "fr-CH", "zh-TW", "fr"]));

        app.AddProjects();

        var server = app.LibreTranslate!.Resource;
        server.Annotations.OfType<ContainerImageAnnotation>().Single().Image.ShouldBe(CoworkeeApp.LibreTranslateImage);
        (await server.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish))["LT_LOAD_ONLY"].ShouldBe("en,de,fr,zh-Hant");
        server.Annotations.OfType<ContainerMountAnnotation>().Single().Target.ShouldBe("/home/libretranslate/.local");
        var api = await app.Apis[0].Resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
        api["Coworkee__Settings__Defaults__Localization.TranslationProvider"].ShouldBe("libreTranslate");
        api["Coworkee__Settings__Defaults__Localization.TranslationEndpoint"].ShouldBe("{demo-translate.bindings.http.url}");
        app.Apis[0].Resource.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource).ShouldContain(server);
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
