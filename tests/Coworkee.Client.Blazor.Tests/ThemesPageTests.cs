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

        Published(page, "Coworkee").ShouldBeTrue();
        Tile(page, "Coworkee").QuerySelector("[data-testid='theme-delete']").ShouldBeNull();
        Tile(page, "Coworkee").QuerySelector("[data-testid='theme-reset']").ShouldBeNull();
        Tile(page, "Coworkee").QuerySelector("[data-testid='theme-is-default']").ShouldNotBeNull();
        Published(page, "Brand").ShouldBeFalse();

        var deleting = page.Find("[data-theme='Brand'] [data-testid='theme-delete']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Delete").ShouldBeTrue());
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Delete").ClickAsync(new());
        await deleting;

        await Api.Received(1).DeleteThemeAsync(Own.Id, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Brand", true)]
    [InlineData("Coworkee", false)]
    public async Task The_switch_on_a_tile_lets_users_choose_the_theme_or_not_also_a_built_in_one(string name, bool published)
    {
        var theme = name == "Brand" ? Own : BuiltIn;
        Api.GetThemesAsync(Arg.Any<CancellationToken>()).Returns([BuiltIn, Own], [BuiltIn with { IsPublished = theme == BuiltIn ? published : true }, Own with { IsPublished = theme == Own ? published : false }]);
        Render<MudSnackbarProvider>();
        var page = Render<Themes>();
        page.WaitForElement($"[data-theme='{name}'] [data-testid='theme-published'] input");

        await page.Find($"[data-theme='{name}'] [data-testid='theme-published'] input").ChangeAsync(new() { Value = published });

        await Api.Received(1).SetThemePublishedAsync(theme.Id, published, Arg.Any<CancellationToken>());
        await Api.DidNotReceiveWithAnyArgs().UpdateThemeAsync(default, default!, default);
        page.WaitForAssertion(() => Published(page, name).ShouldBe(published));
        Services.GetRequiredService<ISnackbar>().ShownSnackbars.ShouldContain(s => s.Message == (published ? $"Theme {name} published" : $"Theme {name} unpublished"));
    }

    [Fact]
    public async Task Editing_a_built_in_theme_saves_it_in_place_under_its_own_name()
    {
        Api.UpdateThemeAsync(BuiltIn.Id, Arg.Any<ThemeRequest>(), Arg.Any<CancellationToken>()).Returns(BuiltIn with { IsChanged = true });
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        var page = Render<Themes>();
        page.WaitForElement("[data-theme='Coworkee'] [data-testid='theme-edit']");

        _ = page.Find("[data-theme='Coworkee'] [data-testid='theme-edit']").ClickAsync(new());

        // the theme editor renders a lot, slow CI runners need the time
        dialogs.WaitForAssertion(() => dialogs.Find("input[data-testid='theme-name'], [data-testid='theme-name'] input").GetAttribute("value").ShouldBe("Coworkee"), TimeSpan.FromSeconds(30));
        dialogs.Find("input[data-testid='theme-name'], [data-testid='theme-name'] input").HasAttribute("readonly").ShouldBeTrue();
        dialogs.Find("[data-testid='theme-editor']").ChildElementCount.ShouldBeGreaterThan(0);
        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='theme-logo']").QuerySelector("[data-testid='theme-logo-choose']").ShouldNotBeNull());
        // the editor renders again while it settles: find the button anew until the click lands
        await dialogs.InvokeAsync(() => { });
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click());

        // the save handler is async: under load the call lands after the click returns
        dialogs.WaitForAssertion(() => _ = Api.Received(1).UpdateThemeAsync(BuiltIn.Id, Arg.Is<ThemeRequest>(r => r.Name == "Coworkee"), Arg.Any<CancellationToken>()));
        await Api.DidNotReceiveWithAnyArgs().CreateThemeAsync(default!, default);
    }

    [Fact]
    public async Task A_changed_built_in_theme_offers_a_reset_to_its_built_in_look()
    {
        var changed = BuiltIn with { IsChanged = true };
        Api.GetThemesAsync(Arg.Any<CancellationToken>()).Returns([changed, Own], [BuiltIn, Own]);
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var page = Render<Themes>();
        page.WaitForElement("[data-theme='Coworkee'] [data-testid='theme-changed']");

        var resetting = page.Find("[data-theme='Coworkee'] [data-testid='theme-reset']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Reset").ShouldBeTrue());
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Reset").ClickAsync(new());
        await resetting;

        await Api.Received(1).ResetThemeAsync(BuiltIn.Id, Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => Tile(page, "Coworkee").QuerySelector("[data-testid='theme-reset']").ShouldBeNull());
    }

    [Fact]
    public void The_sign_in_page_design_moved_to_the_page_sign_in_the_themes_page_links_there()
    {
        var page = Render<Themes>();

        page.WaitForAssertion(() => page.FindAll("[data-testid='theme-tile']").Count.ShouldBe(2));
        page.Find("[data-testid='sign-in-open']").GetAttribute("href")!.ShouldEndWith("/admin/sign-in");
        page.FindAll("[data-testid='sign-in-design']").ShouldBeEmpty();
    }

    [Fact]
    public void No_link_when_the_app_turns_the_sign_in_design_off()
    {
        Services.AddSingleton(new CoworkeeClientOptions { AllowSignInDesign = false });

        var page = Render<Themes>();

        page.WaitForAssertion(() => page.FindAll("[data-testid='theme-tile']").Count.ShouldBe(2));
        page.FindAll("[data-testid='sign-in-link']").ShouldBeEmpty();
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
        Api.GetKeycloakThemeStatusAsync(Arg.Any<CancellationToken>()).Returns(new KeycloakThemeStatusDto(true, "coworkee-demo", null, "The Keycloak login theme could not be written; see the log of the API."));

        var page = Render<Themes>();

        page.WaitForAssertion(() => page.Find("[data-testid='keycloak-theme-error']").TextContent.ShouldBe("The Keycloak sign-in page could not be written. See the log of the API."));
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
    public void The_theme_editor_edits_the_custom_css_as_rules()
    {
        Api.GetThemesAsync(Arg.Any<CancellationToken>()).Returns([BuiltIn, Own with { CustomCss = ".brand { color: red; }" }]);
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var page = Render<Themes>();
        page.WaitForElement("[data-theme='Brand'] [data-testid='theme-edit']");

        _ = page.Find("[data-theme='Brand'] [data-testid='theme-edit']").ClickAsync(new());

        dialogs.WaitForAssertion(() => dialogs.Find("input[data-testid='css-selector']").GetAttribute("value").ShouldBe(".brand"), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Tiles_follow_the_theme_density()
    {
        var page = Render<Themes>();
        page.WaitForAssertion(() => page.Find("[data-testid='theme-built-in']").ClassList.ShouldContain("mud-chip-size-small"));

        await page.InvokeAsync(() => Services.GetRequiredService<ThemeService>().Preview(new CoworkeeTheme { Dense = false }));

        page.WaitForAssertion(() => page.Find("[data-testid='theme-built-in']").ClassList.ShouldContain("mud-chip-size-medium"));
    }

    private static AppConfigurationValuesDto Design(SignInDesign design) =>
        new(SignInDesign.Section, JsonSerializer.SerializeToElement(design, JsonSerializerOptions.Web), JsonSerializer.SerializeToElement(new SignInDesign()), []);

    private static bool Published(IRenderedComponent<Themes> page, string name) =>
        Tile(page, name).QuerySelector("[data-testid='theme-published'] input")!.HasAttribute("checked");

    private static AngleSharp.Dom.IElement Tile(IRenderedComponent<Themes> page, string name) => page.Find($"[data-theme='{name}']");

    private static ThemeDto Theme(string name, bool isGlobal, bool isDefault, bool isPublished) => new(
        Guid.CreateVersion7(), name, isGlobal, isDefault,
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#123456" }),
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#654321" }),
        null, null, null, null, 1, IsPublished: isPublished);
}
