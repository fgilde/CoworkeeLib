using Bunit;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class HeaderConfigurationTests : ClientTestBase
{
    public HeaderConfigurationTests()
    {
        Services.AddSingleton<IAppBarContributor>(new Bar());
        AddAuthorization().SetAuthorized("Ada");
        Api.GetSettingsAsync(SettingScope.Global, default).ReturnsForAnyArgs(
        [
            new SettingValueDto(HeaderSettings.Items, """[{"id":"about","placement":"Bar"},{"id":"search","placement":"Bar"}]""", true),
            new SettingValueDto(AiSettingNames.Display, null, false),
        ]);
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
    }

    [Fact]
    public async Task The_administrator_orders_places_and_hides_the_items_and_picks_where_the_assistant_shows()
    {
        var page = Render<HeaderConfiguration>();
        page.WaitForAssertion(() => page.FindAll("[data-header-item]").Count.ShouldBe(HeaderItems.BuiltIn.Count + 1));
        page.FindAll("[data-header-item]")[0].GetAttribute("data-header-item").ShouldBe(HeaderItems.About);
        // the account menu cannot leave the bar
        page.FindAll($"[data-header-item='{HeaderItems.User}'] [data-testid='header-hide']").ShouldBeEmpty();

        await page.Find($"[data-header-item='{HeaderItems.Search}'] [data-testid='header-up']").ClickAsync(new());
        await page.Find($"[data-header-item='{HeaderItems.RightToLeft}'] [data-testid='header-hide']").ClickAsync(new());
        await page.Find($"[data-header-item='{HeaderItems.Theme}'] [data-testid='header-overflow']").ClickAsync(new());
        await page.Find($"[data-display='{AssistantDisplay.AppBar}']").ClickAsync(new());
        await page.Find("[data-testid='header-save']").ClickAsync(new());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global, Arg.Is<IReadOnlyDictionary<string, string?>>(v =>
            v[AiSettingNames.Display] == AssistantDisplay.AppBar
            && HeaderItems.Parse(v[HeaderSettings.Items]).Take(2).Select(i => i.Id).SequenceEqual(new[] { HeaderItems.Search, HeaderItems.About })
            && HeaderItems.Parse(v[HeaderSettings.Items]).Single(i => i.Id == HeaderItems.RightToLeft).Placement == HeaderPlacement.Hidden
            && HeaderItems.Parse(v[HeaderSettings.Items]).Single(i => i.Id == HeaderItems.Theme).Placement == HeaderPlacement.Overflow), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_goes_back_to_the_default_header()
    {
        var page = Render<HeaderConfiguration>();
        page.WaitForAssertion(() => page.Find("[data-testid='header-reset']"));

        await page.Find("[data-testid='header-reset']").ClickAsync(new());

        page.FindAll("[data-header-item]")[0].GetAttribute("data-header-item").ShouldBe(HeaderItems.Search);
        page.FindAll("[data-header-item]")[1].GetAttribute("data-header-item").ShouldBe("groups");
    }

    private sealed class Bar : IAppBarContributor
    {
        public IEnumerable<AppBarItem> Items => [new(typeof(LayoutCustomizationTests.LanguageSwitch), Id: "groups", Title: "Groups")];
    }
}
