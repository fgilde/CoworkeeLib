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

        page.WaitForElement("[data-setting='Mail.Digest']");
        page.FindAll($"[data-setting='{ThemeSettings.ThemeId}'], [data-setting='{ThemeSettings.Mode}']").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_dark_mode_switch_is_kept_for_signed_in_users()
    {
        Services.AddSingleton<INavigationContributor, AdminNavigation>();
        AddAuthorization().SetAuthorized("Ada");
        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        await layout.Find("button[aria-label='Toggle dark mode']").ClickAsync(new());

        Services.GetRequiredService<ThemeService>().Mode.ShouldBeOneOf("dark", "light");
        await Api.Received(1).SetSettingsAsync(SettingScope.User,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v[ThemeSettings.Mode] == Services.GetRequiredService<ThemeService>().Mode), Arg.Any<CancellationToken>());
    }
}
