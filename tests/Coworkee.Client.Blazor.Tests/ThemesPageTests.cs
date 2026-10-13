using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ThemesPageTests : ClientTestBase
{
    private static readonly ThemeDto BuiltIn = Theme("Coworkee", isGlobal: true, isDefault: true, isPublished: true);
    private static readonly ThemeDto Own = Theme("Brand", isGlobal: false, isDefault: false, isPublished: false);

    public ThemesPageTests()
    {
        Api.GetThemesAsync(Arg.Any<CancellationToken>()).Returns([BuiltIn, Own]);
        Api.GetCurrentThemeAsync(Arg.Any<CancellationToken>()).Returns(BuiltIn);
        Api.GetClientSettingsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string?>());
        Api.GetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>()).Returns(Design(new SignInDesign { Layout = SignInLayouts.Card }));
        Api.SaveAppConfigurationAsync(SignInDesign.Section, Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(call => Design(call.Arg<JsonElement>().Deserialize<SignInDesign>(JsonSerializerOptions.Web)!));
        JSInterop.Setup<MudBlazor.Extensions.Core.CssVariable[]>("MudExCssHelper.getCssVariables", _ => true).SetResult([]);
        Services.AddCascadingValue(sp => sp.GetRequiredService<ThemeService>().DensitySource);
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(ThemePermissions.Manage));
    }

    [Fact]
    public async Task Shows_a_tile_per_theme_and_deletes_only_own_themes_after_confirmation()
    {
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var page = Render<Themes>();
        page.WaitForAssertion(() => page.FindAll("[data-testid='theme-tile']").Count.ShouldBe(2));

        Tile(page, "Coworkee").QuerySelector("[data-testid='theme-published']")!.TextContent.Trim().ShouldBe("Published");
        Tile(page, "Coworkee").QuerySelector("[data-testid='theme-delete']").ShouldBeNull();
        Tile(page, "Coworkee").QuerySelector("[data-testid='theme-is-default']").ShouldNotBeNull();
        Tile(page, "Brand").QuerySelector("[data-testid='theme-published']")!.TextContent.Trim().ShouldBe("Not published");

        var deleting = page.Find("[data-theme='Brand'] [data-testid='theme-delete']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Delete").ShouldBeTrue());
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Delete").ClickAsync(new());
        await deleting;

        await Api.Received(1).DeleteThemeAsync(Own.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Edit_opens_the_theme_editor_for_that_theme_and_a_built_in_one_as_copy()
    {
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var page = Render<Themes>();
        page.WaitForElement("[data-theme='Coworkee'] [data-testid='theme-edit']");

        _ = page.Find("[data-theme='Coworkee'] [data-testid='theme-edit']").ClickAsync(new());

        dialogs.WaitForAssertion(() => dialogs.Find("input[data-testid='theme-name'], [data-testid='theme-name'] input").GetAttribute("value").ShouldBe("Coworkee copy"), TimeSpan.FromSeconds(10));
        dialogs.Find("[data-testid='theme-editor']").ChildElementCount.ShouldBeGreaterThan(0);
        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='theme-logo']").QuerySelector("[data-testid='theme-logo-choose']").ShouldNotBeNull());
    }

    [Fact]
    public async Task The_sign_in_page_card_shows_a_thumbnail_per_layout_and_saves_the_installation_wide_design()
    {
        Render<MudPopoverProvider>();
        var page = Render<Themes>();
        page.WaitForAssertion(() => page.FindAll("[data-testid='sign-in-layout']").Count.ShouldBe(SignInLayouts.All.Count));
        page.Find("[data-layout='Card']").GetAttribute("aria-checked").ShouldBe("true");

        await page.Find("[data-layout='Split']").ClickAsync(new());
        page.Find("input[data-testid='sign-in-media'], [data-testid='sign-in-media'] input").Change("/media/welcome.jpg");
        await page.Find("[data-testid='sign-in-save']").ClickAsync(new());

        await Api.Received(1).SaveAppConfigurationAsync(SignInDesign.Section,
            Arg.Is<JsonElement>(v => v.GetProperty("layout").GetString() == SignInLayouts.Split && v.GetProperty("mediaUrl").GetString() == "/media/welcome.jpg"),
            Arg.Any<CancellationToken>());
        page.Find("[data-layout='Split']").GetAttribute("aria-checked").ShouldBe("true");
    }

    [Theory]
    [InlineData(false, 200)]
    [InlineData(true, 403)]
    public void The_sign_in_page_card_stays_hidden_when_the_app_turns_it_off_or_the_user_may_not_change_the_app_configuration(bool allowed, int status)
    {
        Services.AddSingleton(new CoworkeeClientOptions { AllowSignInDesign = allowed });
        if (status != 200)
        {
            Api.GetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>()).ThrowsAsync(new ApiException(status, "configuration.system_only", null));
        }

        var page = Render<Themes>();

        page.WaitForAssertion(() => page.FindAll("[data-testid='theme-tile']").Count.ShouldBe(2));
        page.FindAll("[data-testid='sign-in-design']").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_keycloak_card_shows_when_the_api_keeps_a_login_theme_and_applies_it_on_request()
    {
        Api.GetKeycloakThemeStatusAsync(Arg.Any<CancellationToken>()).Returns(new KeycloakThemeStatusDto(true, "coworkee-demo", null, null));
        Api.ApplyKeycloakThemeAsync(Arg.Any<CancellationToken>()).Returns(new KeycloakThemeStatusDto(true, "coworkee-demo", DateTimeOffset.UtcNow, null));
        var page = Render<Themes>();
        page.WaitForAssertion(() => page.Find("[data-testid='keycloak-theme-written']").TextContent.Trim().ShouldBe("Not written yet"));

        await page.Find("[data-testid='keycloak-theme-apply']").ClickAsync(new());

        await Api.Received(1).ApplyKeycloakThemeAsync(Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => page.Find("[data-testid='keycloak-theme-written']").TextContent.Trim().ShouldStartWith("Last written"));
    }

    [Fact]
    public void The_keycloak_card_shows_why_writing_failed()
    {
        Api.GetKeycloakThemeStatusAsync(Arg.Any<CancellationToken>()).Returns(new KeycloakThemeStatusDto(true, "coworkee-demo", null, "Access denied"));

        var page = Render<Themes>();

        page.WaitForAssertion(() => page.Find("[data-testid='keycloak-theme-error']").TextContent.ShouldContain("Access denied"));
    }

    [Fact]
    public void The_keycloak_card_stays_hidden_without_a_theme_folder()
    {
        Api.GetKeycloakThemeStatusAsync(Arg.Any<CancellationToken>()).Returns(new KeycloakThemeStatusDto(false, null, null, null));

        var page = Render<Themes>();

        page.WaitForAssertion(() => page.FindAll("[data-testid='theme-tile']").Count.ShouldBe(2));
        page.FindAll("[data-testid='keycloak-theme']").ShouldBeEmpty();
    }

    [Fact]
    public void The_theme_editor_edits_the_custom_css_as_multiline_text()
    {
        Api.GetThemesAsync(Arg.Any<CancellationToken>()).Returns([BuiltIn, Own with { CustomCss = ".brand { color: red; }" }]);
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var page = Render<Themes>();
        page.WaitForElement("[data-theme='Brand'] [data-testid='theme-edit']");

        _ = page.Find("[data-theme='Brand'] [data-testid='theme-edit']").ClickAsync(new());

        dialogs.WaitForAssertion(() => dialogs.FindAll("textarea").Any(t => t.TextContent.Contains(".brand { color: red; }") || t.GetAttribute("value") == ".brand { color: red; }")
            .ShouldBeTrue(), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Tiles_follow_the_theme_density()
    {
        var page = Render<Themes>();
        page.WaitForAssertion(() => page.Find("[data-testid='theme-published']").ClassList.ShouldContain("mud-chip-size-small"));

        await page.InvokeAsync(() => Services.GetRequiredService<ThemeService>().Preview(new CoworkeeTheme { Dense = false }));

        page.WaitForAssertion(() => page.Find("[data-testid='theme-published']").ClassList.ShouldContain("mud-chip-size-medium"));
    }

    private static AppConfigurationValuesDto Design(SignInDesign design) =>
        new(SignInDesign.Section, JsonSerializer.SerializeToElement(design, JsonSerializerOptions.Web), JsonSerializer.SerializeToElement(new SignInDesign()), []);

    private static AngleSharp.Dom.IElement Tile(IRenderedComponent<Themes> page, string name) => page.Find($"[data-theme='{name}']");

    private static ThemeDto Theme(string name, bool isGlobal, bool isDefault, bool isPublished) => new(
        Guid.CreateVersion7(), name, isGlobal, isDefault,
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#123456" }),
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#654321" }),
        null, null, null, null, 1, IsPublished: isPublished);
}
