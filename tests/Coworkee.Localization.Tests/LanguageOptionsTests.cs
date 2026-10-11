using System.Net.Http.Json;
using Coworkee.Contracts.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Localization.Tests;

public sealed class LanguageOptionsTests(LocalizationApp app)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Without_languages_english_and_the_shipped_cultures_start_and_english_is_the_default()
    {
        new LocalizationOptions().Initial(["de"]).ShouldBe([("en", true), ("de", false)]);
        new LocalizationOptions { Languages = ["de", "fr", "de"] }.Initial(["de"]).ShouldBe([("de", true), ("fr", false)]);
        new LocalizationOptions { Languages = ["en", "de"], DefaultLanguage = "de" }.Initial([]).ShouldBe([("en", false), ("de", true)]);
    }

    [Fact]
    public async Task Configured_languages_are_stored_at_setup_with_their_default_and_stay_editable()
    {
        var options = app.App.Services.GetRequiredService<IOptions<LocalizationOptions>>().Value;
        options.Languages = ["de", "fr"];
        options.DefaultLanguage = "de";
        try
        {
            var setup = await app.SetupAsync();
            var admin = app.As(setup.AdminUserId, setup.TenantId);

            var languages = (await admin.GetFromJsonAsync<LanguageDto[]>("/api/v1/localization/languages/all", Ct))!;
            languages.Select(l => (l.Culture, l.IsDefault, l.Id is not null)).ShouldBe([("de", true, true), ("fr", false, true)], ignoreOrder: true);
            (await admin.PutAsJsonAsync("/api/v1/localization/languages/fr/enabled", new SetLanguageEnabledRequest(false), Ct)).EnsureSuccessStatusCode();
            (await app.Anonymous().GetFromJsonAsync<LanguageDto[]>("/api/v1/localization/languages", Ct))!.Select(l => l.Culture).ShouldBe(["de"]);
        }
        finally
        {
            options.Languages = [];
            options.DefaultLanguage = null;
        }
    }
}
