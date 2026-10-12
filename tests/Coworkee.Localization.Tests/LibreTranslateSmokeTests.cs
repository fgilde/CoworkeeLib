using Coworkee.Contracts.Localization;
using Coworkee.Localization.MachineTranslation;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coworkee.Localization.Tests;

/// <summary>A real LibreTranslate container (models en and de only); the first run downloads them, which takes a minute or two.</summary>
[Trait("Category", "Docker")]
public sealed class LibreTranslateSmokeTests : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder("libretranslate/libretranslate:v1.9.6")
        .WithEnvironment("LT_LOAD_ONLY", "en,de")
        .WithPortBinding(5000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(5000).ForPath("/health"), w => w.WithTimeout(TimeSpan.FromMinutes(10))))
        .Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task English_texts_come_back_in_german_with_their_placeholders()
    {
        var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var settings = new Dictionary<string, string?>
        {
            [LocalizationSettings.TranslationProvider] = TranslationProviders.LibreTranslate,
            [LocalizationSettings.TranslationEndpoint] = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(5000)}",
        };
        var translator = new TextTranslator([new LibreTranslator(services.GetRequiredService<IHttpClientFactory>())], new TranslationProviderTests.FixedSettings(settings), NullLogger<TextTranslator>.Instance);

        var result = await translator.TranslateAsync(["Good morning", "{0} files were deleted.", "Users & roles", "Delete {0}? This cannot be undone."], "en", "de", TestContext.Current.CancellationToken);

        result["Good morning"].ShouldBe("Guten Morgen");
        result["{0} files were deleted."].ShouldContain("{0}");
        result["{0} files were deleted."].ShouldContain("gelöscht");
        result["Users & roles"].ShouldContain("&");
        result["Delete {0}? This cannot be undone."].ShouldContain("löschen?");
    }
}
