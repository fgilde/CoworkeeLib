using Bunit;
using Coworkee.Client.Blazor.Components;
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
    public void The_stored_language_translates_navigation_and_offers_the_others()
    {
        JSInterop.Setup<string?>("localStorage.getItem", "coworkee.culture").SetResult("de");

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

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
}
