using Bunit;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class MailTemplateDetailTests : ClientTestBase
{
    public MailTemplateDetailTests()
    {
        Api.GetMailTemplatesAsync(default).ReturnsForAnyArgs([new MailTemplateSummaryDto("Identity.Welcome", "Welcome", ["de", "en"], [], false, null)]);
        Api.GetMailTemplateAsync(default!, default!).ReturnsForAnyArgs(call =>
            new MailTemplateDto("Identity.Welcome", call.ArgAt<string>(1), "Hi", "<p>hi</p>", "Hi", "<p>hi</p>", false, [], false));
        Localization.GetLanguagesAsync(default, default).ReturnsForAnyArgs([
            new LanguageDto(null, "en", "English", true, true), new LanguageDto(null, "de", "Deutsch", true, false), new LanguageDto(null, "fr", "Français", true, false)]);
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
    }

    [Fact]
    public async Task Language_chips_switch_between_the_variants_and_mark_the_open_one()
    {
        var page = Render<MailTemplateDetail>(p => p.Add(d => d.Name, "Identity.Welcome").Add(d => d.Culture, "en"));

        page.WaitForAssertion(() => page.Find("[data-testid='language-chip-en']").ClassList.ShouldContain("mud-chip-filled"));
        page.Find("[data-testid='language-chip-de']").ClassList.ShouldContain("mud-chip-outlined");
        page.Find("[data-testid='add-language']").HasAttribute("disabled").ShouldBeFalse();
        page.Markup.ShouldContain("Welcome");

        await page.InvokeAsync(() => page.Find("[data-testid='language-chip-de']").ClickAsync(new()));

        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/admin/mail/templates/Identity.Welcome/de");
    }
}
