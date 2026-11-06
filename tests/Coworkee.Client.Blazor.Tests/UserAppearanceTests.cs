using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class UserAppearanceTests : ClientTestBase
{
    private static readonly ThemeDto Brand = Theme("Brand", isDefault: true);
    private static readonly ThemeDto Ocean = Theme("Ocean");

    public UserAppearanceTests()
    {
        Api.GetThemesAsync(Arg.Any<CancellationToken>()).Returns([Brand, Ocean]);
        Api.GetCurrentThemeAsync(Arg.Any<CancellationToken>()).Returns(Brand);
        Api.GetClientSettingsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string?>());
        Api.GetSettingDefinitionsAsync(true, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingGroupDto("Appearance", "Appearance",
            [
                new SettingDefinitionDto(ThemeSettings.Mode, "Color mode", null, SettingType.Choice, "system", [SettingScope.User], ["system", "light", "dark"]),
                new SettingDefinitionDto(ThemeSettings.ThemeId, "Theme", null, SettingType.String, null, [SettingScope.User], null),
            ]),
            new SettingGroupDto("Mail", "Mail", [new SettingDefinitionDto("Mail.Digest", "Digest", null, SettingType.Bool, "true", [SettingScope.User], null)]),
        ]);
        Api.GetSettingsAsync(SettingScope.User, Arg.Any<CancellationToken>()).Returns([]);
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
    }

    private static ThemeDto Theme(string name, bool isDefault = false) => new(
        Guid.CreateVersion7(), name, true, isDefault,
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#123456" }),
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#654321" }),
        null, null, null, null, 1);

    [Fact]
    public async Task Picking_a_theme_applies_it_and_keeps_it_for_the_user()
    {
        AddAuthorization().SetAuthorized("Ada");
        var page = Render<UserSettings>();

        page.WaitForElement("[data-theme='Ocean']");
        await page.Find("[data-theme='Ocean']").ClickAsync(new());

        Services.GetRequiredService<ThemeService>().Current!.Name.ShouldBe("Ocean");
        await Api.Received(1).SetSettingsAsync(SettingScope.User,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v[ThemeSettings.ThemeId] == Ocean.Id.ToString()), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Theme_cards_preview_the_app_bar_surface_and_text_of_the_theme()
    {
        AddAuthorization().SetAuthorized("Ada");
        var page = Render<UserSettings>();

        var ocean = page.WaitForElement("[data-theme='Ocean']");

        ocean.QuerySelector(".cw-theme-preview-bar")!.GetAttribute("style")!.ShouldContain("#123456");
        ocean.QuerySelector(".cw-theme-preview-surface").ShouldNotBeNull();
        ocean.QuerySelector(".cw-theme-preview-text").ShouldNotBeNull();
        await page.Find("[data-mode='dark']").ClickAsync(new());
        page.Find("[data-theme='Ocean'] .cw-theme-preview-bar").GetAttribute("style")!.ShouldContain("#654321", Case.Insensitive, "dark mode previews the dark palette");
    }

    [Fact]
    public async Task The_theme_menu_shows_swatches_and_switches_the_mode_in_place()
    {
        AddAuthorization().SetAuthorized("Ada");
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var menu = Render<ThemeMenu>();

        await menu.WaitForElement("[data-testid='theme-menu'] button").ClickAsync(new());
        var ocean = popovers.WaitForElement("[data-theme='Ocean']");
        ocean.QuerySelectorAll(".cw-theme-dot").Select(d => d.GetAttribute("style")).ShouldContain(s => s!.Contains("#123456"));
        await popovers.Find("[data-testid='theme-modes'] [data-mode='dark']").ClickAsync(new());

        Services.GetRequiredService<ThemeService>().Mode.ShouldBe("dark");
        await Api.Received(1).SetSettingsAsync(SettingScope.User,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v[ThemeSettings.Mode] == "dark"), Arg.Any<CancellationToken>());
        popovers.Find("[data-testid='theme-modes'] [data-mode='dark']").GetAttribute("aria-pressed").ShouldBe("true");
    }

    [Fact]
    public async Task A_theme_the_user_chose_and_someone_edits_applies_at_the_next_reload()
    {
        Api.GetClientSettingsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string?> { [ThemeSettings.ThemeId] = Ocean.Id.ToString() });
        var service = Services.GetRequiredService<ThemeService>();
        await service.LoadAsync();
        service.Theme.ShowLogoInNav.ShouldBeTrue();

        Api.GetThemesAsync(Arg.Any<CancellationToken>()).Returns([Brand, Ocean with { Options = JsonSerializer.SerializeToElement(new { ShowLogoInNav = false }), Revision = 2 }]);
        await service.LoadAsync();

        service.Current!.Name.ShouldBe("Ocean");
        service.Theme.ShowLogoInNav.ShouldBeFalse();
    }

    [Fact]
    public async Task A_deleted_theme_the_user_chose_gives_way_to_the_organisation_theme_and_leaves_the_menu()
    {
        Api.GetClientSettingsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string?> { [ThemeSettings.ThemeId] = Ocean.Id.ToString() });
        AddAuthorization().SetAuthorized("Ada");
        var service = Services.GetRequiredService<ThemeService>();
        await service.LoadAsync();
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var menu = Render<ThemeMenu>();
        await menu.WaitForElement("[data-testid='theme-menu'] button").ClickAsync(new());
        popovers.WaitForElement("[data-theme='Ocean']");

        Api.GetThemesAsync(Arg.Any<CancellationToken>()).Returns([Brand, Theme("Forest")]);
        await menu.InvokeAsync(service.LoadAsync);

        service.Current!.Name.ShouldBe("Brand");
        popovers.WaitForAssertion(() => popovers.FindAll("[data-theme='Ocean']").ShouldBeEmpty());
        popovers.FindAll("[data-theme='Forest']").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_theme_cards_of_the_user_settings_follow_deleted_and_new_themes()
    {
        AddAuthorization().SetAuthorized("Ada");
        var page = Render<UserSettings>();
        page.WaitForElement("[data-theme='Ocean']");

        Api.GetThemesAsync(Arg.Any<CancellationToken>()).Returns([Brand, Theme("Forest")]);
        await page.InvokeAsync(Services.GetRequiredService<ThemeService>().LoadAsync);

        page.WaitForAssertion(() => page.FindAll("[data-theme='Ocean']").ShouldBeEmpty());
        page.FindAll("[data-theme='Forest']").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_organisation_default_clears_the_users_choice()
    {
        AddAuthorization().SetAuthorized("Ada");
        var page = Render<UserSettings>();
        page.WaitForElement("[data-theme='Ocean']");
        await page.Find("[data-theme='Ocean']").ClickAsync(new());

        await page.Find("[data-testid='theme-default']").ClickAsync(new());

        Services.GetRequiredService<ThemeService>().Current!.Name.ShouldBe("Brand");
        await Api.Received(1).SetSettingsAsync(SettingScope.User,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v.ContainsKey(ThemeSettings.ThemeId) && v[ThemeSettings.ThemeId] == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_raw_theme_fields_are_not_in_the_settings_form()
    {
        AddAuthorization().SetAuthorized("Ada");
        var page = Render<UserSettings>();

        page.WaitForAssertion(() => SettingsFormTests.Labels(page).ShouldContain("Digest"));
        SettingsFormTests.Labels(page).ShouldNotContain("Theme");
        SettingsFormTests.Labels(page).ShouldNotContain("Color mode");
    }

    [Fact]
    public async Task The_dark_mode_switch_is_kept_for_signed_in_users()
    {
        Services.AddSingleton<INavigationContributor, AdminNavigation>();
        AddAuthorization().SetAuthorized("Ada");
        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        await layout.Find("button[aria-label='Dark mode']").ClickAsync(new());

        Services.GetRequiredService<ThemeService>().Mode.ShouldBeOneOf("dark", "light");
        await Api.Received(1).SetSettingsAsync(SettingScope.User,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v[ThemeSettings.Mode] == Services.GetRequiredService<ThemeService>().Mode), Arg.Any<CancellationToken>());
    }
}
