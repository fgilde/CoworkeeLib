using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Localization;

namespace Coworkee.Localization.Tests;

public sealed class LocalizationTests(LocalizationApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Without_stored_languages_english_and_every_shipped_culture_are_offered()
    {
        var languages = (await app.Anonymous().GetFromJsonAsync<LanguageDto[]>("/api/v1/localization/languages", Ct))!;

        languages.Select(l => l.Culture).ShouldBe(["de", "en"], ignoreOrder: true);
        languages.Single(l => l.IsDefault).Culture.ShouldBe("en");
    }

    [Fact]
    public async Task Texts_merge_code_and_embedded_resources_and_regions_fall_back_to_the_language()
    {
        var texts = await TextsAsync("de-AT");

        texts["Brands"].ShouldBe("Marken");
        texts["Products"].ShouldBe("Produkte");
    }

    [Fact]
    public async Task Edits_of_administrators_win_and_an_empty_edit_restores_the_default()
    {
        (await Admin.PutAsJsonAsync("/api/v1/localization/translations", new SetTranslationRequest("de", "Brands", "Handelsmarken"), Ct)).EnsureSuccessStatusCode();
        (await TextsAsync("de"))["Brands"].ShouldBe("Handelsmarken");

        (await Admin.PutAsJsonAsync("/api/v1/localization/translations", new SetTranslationRequest("de", "Brands", null), Ct)).EnsureSuccessStatusCode();
        (await TextsAsync("de"))["Brands"].ShouldBe("Marken");
    }

    [Fact]
    public async Task Keys_clients_miss_show_up_in_the_editor_only_for_managers()
    {
        (await Admin.PostAsJsonAsync("/api/v1/localization/missing", new MissingTextsRequest(["Invoices", "Brands", "Invoices"]), Ct)).EnsureSuccessStatusCode();

        var rows = (await Admin.GetFromJsonAsync<TranslationRowDto[]>("/api/v1/localization/translations/de", Ct))!;

        rows.Single(r => r.Key == "Invoices").Default.ShouldBeNull();
        rows.Single(r => r.Key == "Brands").Default.ShouldBe("Marken");
        (await app.As(Guid.CreateVersion7(), _setup.TenantId).GetAsync("/api/v1/localization/translations/de", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Stored_languages_replace_the_shipped_list_and_the_default_cannot_be_deleted()
    {
        var french = await (await Admin.PostAsJsonAsync("/api/v1/localization/languages", new AddEditLanguageRequest { Culture = "fr", Name = "Francais", IsDefault = true }, Ct))
            .Content.ReadFromJsonAsync<LanguageDto>(Ct);
        (await Admin.PostAsJsonAsync("/api/v1/localization/languages", new AddEditLanguageRequest { Culture = "xx-nope", Name = "Nope" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await app.Anonymous().GetFromJsonAsync<LanguageDto[]>("/api/v1/localization/languages", Ct))!.Select(l => l.Culture).ShouldBe(["fr"]);
        (await Admin.PostAsJsonAsync("/api/v1/localization/languages/delete", new IdListRequest([french!.Id!.Value]), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private async Task<IReadOnlyDictionary<string, string>> TextsAsync(string culture) =>
        (await app.Anonymous().GetFromJsonAsync<TextsDto>($"/api/v1/localization/texts/{culture}", Ct))!.Texts;
}
