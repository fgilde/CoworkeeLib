using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;

namespace Coworkee.Localization.Tests;

public sealed class LanguageSwitchTests(LocalizationApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Cultures_are_switched_on_and_off_and_the_default_stays_on()
    {
        var on = await SwitchAsync("fr-CH", true);

        (on.Language.IsEnabled, on.Translated, on.TranslatorAvailable).ShouldBe((true, 0, false));
        var languages = (await Admin.GetFromJsonAsync<LanguageDto[]>("/api/v1/localization/languages/all", Ct))!;
        languages.Select(l => l.Culture).ShouldBe(["de", "en", "fr-CH"], ignoreOrder: true);

        (await SwitchAsync("fr-CH", false)).Language.IsEnabled.ShouldBeFalse();
        (await app.Anonymous().GetFromJsonAsync<LanguageDto[]>("/api/v1/localization/languages", Ct))!.Select(l => l.Culture).ShouldNotContain("fr-CH");
        (await Admin.PutAsJsonAsync("/api/v1/localization/languages/en/enabled", new SetLanguageEnabledRequest(false), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task With_a_translator_key_switching_on_translates_what_the_language_lacks()
    {
        (await Admin.PutAsJsonAsync("/api/v1/settings/global",
            new SetSettingsRequest(new Dictionary<string, string?> { [LocalizationSettings.TranslatorKey] = "test-key" }), Ct)).EnsureSuccessStatusCode();

        var on = await SwitchAsync("fr", true);

        on.Translated.ShouldBeGreaterThan(0);
        var texts = (await app.Anonymous().GetFromJsonAsync<TextsDto>("/api/v1/localization/texts/fr", Ct))!.Texts;
        texts["Brands"].ShouldBe("fr:Brands");
        texts.Where(t => t.Key.Contains("{0}", StringComparison.Ordinal)).ShouldAllBe(t => t.Value.Contains("{0}", StringComparison.Ordinal));
        var again = await (await Admin.PostAsync("/api/v1/localization/translations/fr/translate", null, Ct)).Content.ReadFromJsonAsync<TranslateMissingDto>(Ct);
        again!.Translated.ShouldBe(0);
    }

    private async Task<LanguageSwitchDto> SwitchAsync(string culture, bool enabled)
    {
        var response = await Admin.PutAsJsonAsync($"/api/v1/localization/languages/{culture}/enabled", new SetLanguageEnabledRequest(enabled), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LanguageSwitchDto>(Ct))!;
    }
}
