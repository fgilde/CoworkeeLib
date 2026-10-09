using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class LocalizationTests : ClientTestBase
{
    public LocalizationTests()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
        Localization.GetLanguagesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new LanguageDto(null, "en", "English", true, true), new LanguageDto(null, "de", "Deutsch", true, false)]);
        Localization.GetTextsAsync("de", Arg.Any<CancellationToken>()).Returns(new TextsDto("de", new Dictionary<string, string> { ["Products"] = "Produkte", ["Catalog"] = "Katalog" }));
        Services.AddSingleton<INavigationContributor>(new LayoutCustomizationTests.StaticNavigation(
            new CoworkeeNavItem("Products", "/catalog/products", Icons.Material.Outlined.Inventory, Group: "Catalog")));
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(LocalizationPermissions.Manage));
    }

    [Fact]
    public async Task The_stored_language_translates_navigation_and_offers_the_others()
    {
        JSInterop.Setup<string?>("localStorage.getItem", "coworkee.culture").SetResult("de");

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        await OpenNavigationAsync(layout);

        layout.WaitForAssertion(() => layout.Markup.ShouldContain("Produkte"));
        layout.Markup.ShouldContain("Katalog");
        layout.FindAll("[data-testid='language-menu']").ShouldNotBeEmpty();
    }

    [Fact]
    public void Translations_are_edited_per_key_and_an_empty_value_restores_the_default()
    {
        Localization.GetTranslationRowsAsync("de", Arg.Any<CancellationToken>())
            .Returns([new TranslationRowDto("Brands", "Marken", null), new TranslationRowDto("Invoices", null, null)]);
        Render<MudPopoverProvider>();
        var page = Render<Translations>();

        page.WaitForAssertion(() => page.FindAll("[data-key]").Count.ShouldBe(2));
        page.Find("[data-key='Invoices'] input, input[data-key='Invoices']").Input("Rechnungen");
        page.WaitForAssertion(() => Localization.Received(1).SetTranslationAsync(new SetTranslationRequest("de", "Invoices", "Rechnungen"), Arg.Any<CancellationToken>()), TimeSpan.FromSeconds(10));

        page.Find("[data-key='Brands'] input, input[data-key='Brands']").Input("");
        page.WaitForAssertion(() => Localization.Received(1).SetTranslationAsync(new SetTranslationRequest("de", "Brands", null), Arg.Any<CancellationToken>()), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Switching_off_the_current_language_moves_the_client_to_the_default()
    {
        var localizer = Services.GetRequiredService<Localization.CoworkeeLocalizer>();
        await localizer.UseAsync("de");
        Localization.GetLanguagesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns([new LanguageDto(Guid.NewGuid(), "en", "English", true, true)]);

        (await localizer.RefreshAsync()).ShouldBe("en");
        localizer.Culture.ShouldBe("en");
    }

    [Fact]
    public async Task Cultures_are_grouped_by_language_and_switched_with_one_click()
    {
        Localization.SetLanguageEnabledAsync("fr-CH", true, Arg.Any<CancellationToken>())
            .Returns(new LanguageSwitchDto(new LanguageDto(Guid.NewGuid(), "fr-CH", "Français (Suisse)", true, false), 12, true));
        Render<MudSnackbarProvider>();
        var page = Render<Languages>();

        page.Find("input[data-testid='language-filter'], [data-testid='language-filter'] input").Input("fr-CH");
        page.WaitForAssertion(() => page.Find("[data-culture='fr-CH'] input[type='checkbox']"));
        await page.Find("[data-culture='fr-CH'] input[type='checkbox']").ChangeAsync(new ChangeEventArgs { Value = true });

        await Localization.Received(1).SetLanguageEnabledAsync("fr-CH", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Translations_editor_shows_a_row_per_language_adds_typed_cultures_and_removes_rows()
    {
        // here, as the languages of the app set the process culture other test classes would race with
        await Services.GetRequiredService<Localization.CoworkeeLocalizer>().InitializeAsync(null);
        IReadOnlyDictionary<string, string>? value = new Dictionary<string, string> { ["de"] = "Reisepass" };
        Render<MudPopoverProvider>();
        var editor = Render<TranslationsEditor>(p => p.Add(e => e.Value, value).Add(e => e.ValueChanged, v => value = v));

        editor.Find("[data-culture='de']").TextContent.ShouldContain("Deutsch");
        editor.Find("[data-culture='de'] input").Change("Pass");
        value.ShouldBe(new Dictionary<string, string> { ["de"] = "Pass" });

        await AddLanguageAsync(editor, "fr-CA");
        editor.WaitForAssertion(() => value.Keys.ShouldBe(["de", "fr-CA"]));
        editor.Find("[data-culture='fr-CA'] input").Change("Passeport");
        value["fr-CA"].ShouldBe("Passeport");

        await AddLanguageAsync(editor, "not a language");
        editor.WaitForAssertion(() => editor.Markup.ShouldContain("Not a language code"));

        await editor.Find("[data-culture='de'] [data-testid='translation-remove']").ClickAsync(new());
        value.Keys.ShouldBe(["fr-CA"]);
    }

    private static async Task AddLanguageAsync(IRenderedComponent<TranslationsEditor> editor, string text)
    {
        const string input = "input[data-testid='translation-language'], [data-testid='translation-language'] input";
        editor.Find(input).Input(text);
        await editor.Find(input).KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
    }
}
