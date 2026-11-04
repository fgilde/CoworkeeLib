using Bunit;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class HeaderConfigurationTests : ClientTestBase
{
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

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
        _dialogs = Render<MudDialogProvider>();
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
    public async Task Dragging_orders_the_items_and_dropping_on_an_area_places_them_there_except_the_account_menu()
    {
        var page = Render<HeaderConfiguration>();
        page.WaitForAssertion(() => page.FindAll("[data-header-item]").Count.ShouldBe(HeaderItems.BuiltIn.Count + 1));
        var drop = page.FindComponent<MudDropContainer<HeaderItem>>().Instance.ItemDropped;
        HeaderItem Item(string id) => HeaderItems.BuiltIn.Single(i => i.Id == id);

        await page.InvokeAsync(() => drop.InvokeAsync(new MudItemDropInfo<HeaderItem>(Item(HeaderItems.Search), nameof(HeaderPlacement.Overflow), 0)));
        await page.InvokeAsync(() => drop.InvokeAsync(new MudItemDropInfo<HeaderItem>(Item(HeaderItems.RightToLeft), nameof(HeaderPlacement.Bar), 0)));
        await page.InvokeAsync(() => drop.InvokeAsync(new MudItemDropInfo<HeaderItem>(Item(HeaderItems.Theme), nameof(HeaderPlacement.Hidden), 0)));
        await page.InvokeAsync(() => drop.InvokeAsync(new MudItemDropInfo<HeaderItem>(Item(HeaderItems.User), nameof(HeaderPlacement.Hidden), 0)));

        page.WaitForAssertion(() => page.FindAll("[data-header-item]")[0].GetAttribute("data-header-item").ShouldBe(HeaderItems.RightToLeft));
        page.Find($"[data-header-zone='{HeaderPlacement.Overflow}'] [data-header-item]").GetAttribute("data-header-item").ShouldBe(HeaderItems.Search);
        page.Find($"[data-header-item='{HeaderItems.Search}']").GetAttribute("data-placement").ShouldBe(nameof(HeaderPlacement.Overflow));
        page.Find($"[data-header-item='{HeaderItems.User}']").GetAttribute("data-placement").ShouldBe(nameof(HeaderPlacement.Bar));
        await page.Find("[data-testid='header-save']").ClickAsync(new());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global, Arg.Is<IReadOnlyDictionary<string, string?>>(v =>
            HeaderItems.Parse(v[HeaderSettings.Items]).Take(2).Select(i => i.Id).SequenceEqual(new[] { HeaderItems.RightToLeft, HeaderItems.About })
            && HeaderItems.Parse(v[HeaderSettings.Items]).Single(i => i.Id == HeaderItems.Search).Placement == HeaderPlacement.Overflow
            && HeaderItems.Parse(v[HeaderSettings.Items]).Single(i => i.Id == HeaderItems.Theme).Placement == HeaderPlacement.Hidden
            && HeaderItems.Parse(v[HeaderSettings.Items]).Single(i => i.Id == HeaderItems.User).Placement == HeaderPlacement.Bar), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_item_can_be_limited_to_users_with_permissions()
    {
        Api.GetPermissionDefinitionsAsync(Arg.Any<CancellationToken>()).Returns(
            [new PermissionGroupDto("Mail", "Mail", [new PermissionDto("Mail.A", "A", []), new PermissionDto("Mail.B", "B", [])])]);
        var page = Render<HeaderConfiguration>();
        page.WaitForAssertion(() => page.Find($"[data-header-item='{HeaderItems.About}'] [data-testid='header-access']"));
        // the account menu stays for everyone
        page.FindAll($"[data-header-item='{HeaderItems.User}'] [data-testid='header-access']").ShouldBeEmpty();

        page.Find($"[data-header-item='{HeaderItems.About}'] [data-testid='header-access']").ClassList.ShouldNotContain("mud-warning-text");

        // the permissions are chosen in a side sheet, not below the list
        var editing = page.Find($"[data-header-item='{HeaderItems.About}'] [data-testid='header-access']").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid='header-access-dialog'] [data-picker-permission='Mail.A'] input");
        // found and used on the renderer's thread, so no render of the sheet comes in between
        await _dialogs.InvokeAsync(() => _dialogs.Find("[data-testid='header-access-dialog'] [data-picker-permission='Mail.A'] input").ChangeAsync(new() { Value = true }));
        await _dialogs.InvokeAsync(() => _dialogs.Find("[data-testid='header-access-dialog'] [data-picker-permission='Mail.B'] input").ChangeAsync(new() { Value = true }));
        await _dialogs.InvokeAsync(() => _dialogs.Find("[data-testid='header-access-dialog'] [data-match='All']").ClickAsync(new()));
        await _dialogs.InvokeAsync(() => _dialogs.Find("[data-testid='header-access-apply']").ClickAsync(new()));
        await editing;

        // the lock shows the restriction at once
        page.WaitForAssertion(() => page.Find($"[data-header-item='{HeaderItems.About}'] [data-testid='header-access']").ClassList.ShouldContain("mud-warning-text"));
        page.FindAll("[data-testid='header-access-panel']").ShouldBeEmpty();
        await page.Find("[data-testid='header-save']").ClickAsync(new());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global, Arg.Is<IReadOnlyDictionary<string, string?>>(v =>
            HeaderItems.Parse(v[HeaderSettings.Items]).Single(i => i.Id == HeaderItems.About).Permissions!.SequenceEqual(new[] { "Mail.A", "Mail.B" })
            && HeaderItems.Parse(v[HeaderSettings.Items]).Single(i => i.Id == HeaderItems.About).Match == PermissionMatch.All
            && HeaderItems.Parse(v[HeaderSettings.Items]).Single(i => i.Id == HeaderItems.Search).Permissions == null), Arg.Any<CancellationToken>());
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
