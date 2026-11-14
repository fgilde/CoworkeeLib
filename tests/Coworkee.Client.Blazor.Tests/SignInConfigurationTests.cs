using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Files;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static Coworkee.Client.Blazor.Tests.SettingsFormTests;

namespace Coworkee.Client.Blazor.Tests;

[Collection(nameof(LocalizationTests))]
public sealed class SignInConfigurationTests : ClientTestBase
{
    private static readonly SettingScope[] Global = [SettingScope.Global];
    private static readonly SettingScope[] Both = [SettingScope.Global, SettingScope.Tenant];

    public SignInConfigurationTests()
    {
        Definitions();
        Api.GetSettingsAsync(Arg.Any<SettingScope>(), Arg.Any<CancellationToken>()).Returns([new SettingValueDto(AuthSettings.MaxSessionsPerUser, "3", true)]);
        Api.GetAppConfigurationAsync(CoworkeeAppSettings.Section, Arg.Any<CancellationToken>()).Returns(AppSettings(new CoworkeeAppSettings
        {
            Registration = new RegistrationOptions { RequireAddress = true, AllowedEmails = ["*@acme.test"] },
            Jobs = new BackgroundJobOptions { WorkerCount = 7 },
        }));
        Api.SaveAppConfigurationAsync(CoworkeeAppSettings.Section, Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(call => AppSettings(call.Arg<JsonElement>().Deserialize<CoworkeeAppSettings>(JsonSerializerOptions.Web)!));
        Api.GetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>()).Returns(Design(new SignInDesign { Layout = SignInLayouts.Card }));
        Api.SaveAppConfigurationAsync(SignInDesign.Section, Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(call => Design(call.Arg<JsonElement>().Deserialize<SignInDesign>(JsonSerializerOptions.Web)!));
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([FilePermissions.View]);
        Services.AddSingleton(new ClientAppConfiguration(CoworkeeAppSettings.Section, "App settings", typeof(CoworkeeAppSettings), null, IsAppSettings: true));
    }

    [Fact]
    public void Arranges_the_settings_by_topic_in_tabs_with_the_registration_and_the_design_of_the_app_configuration()
    {
        var page = RenderPage();

        page.WaitForAssertion(() => Tabs(page).ShouldBe(["Methods", "Security", "Registration", "Appearance", "Texts"]));
        page.Find("[data-testid='sign-in-login-mode-toggle'] [data-mode='Both']");
        Labels(page).ShouldContain("Passkeys");

        Open(page, "Security");
        Input(page, "Sessions per user").GetAttribute("value").ShouldBe("3");
        Labels(page).ShouldContain("Minimum password length");
        page.Find("[data-testid='sign-in-password'] [data-setting='Account.Password.RequiredLength'] .mud-input-adornment").TextContent.Trim().ShouldBe("characters");
        // a setting of the two groups the page does not arrange itself still shows, other groups do not
        page.Find("[data-testid='sign-in-more']").TextContent.ShouldContain("Act within one role");
        Labels(page).ShouldNotContain("Cache minutes");

        Open(page, "Registration");
        Labels(page).ShouldContain("Allow self registration");
        Input(page, "Ask for the address").HasAttribute("checked").ShouldBeTrue();
        page.Find("[data-testid='sign-in-allowed-emails']").TextContent.ShouldContain("*@acme.test");
        page.Find("[data-testid='sign-in-documents-none']");

        Open(page, "Appearance");
        page.Find("[data-testid='sign-in-design'] [data-layout='Card']").GetAttribute("aria-checked").ShouldBe("true");

        Open(page, "Texts");
        Labels(page).ShouldContain("Show the app name");
        Labels(page).ShouldContain("Title");
        Labels(page).ShouldContain("Notice");
    }

    [Fact]
    public void Without_an_external_provider_there_is_no_method_to_choose()
    {
        Definitions(loginMode: false);

        var page = RenderPage();

        page.WaitForAssertion(() => page.Find("[data-testid='sign-in-no-provider']"));
        page.FindAll("[data-testid='sign-in-login-mode-toggle']").ShouldBeEmpty();
        Labels(page).ShouldContain("Passkeys");
    }

    [Fact]
    public void A_mode_the_host_fixes_shows_selected_and_read_only_with_a_hint()
    {
        Definitions(fixedMode: nameof(LoginMode.External));

        var page = RenderPage();

        page.WaitForAssertion(() => page.Find("[data-testid='sign-in-login-mode-fixed']").TextContent
            .ShouldContain("The host fixes External in its configuration; the stored value applies once it no longer does."));
        var toggle = page.FindComponent<MudToggleGroup<string>>().Instance;
        toggle.Disabled.ShouldBeTrue();
        toggle.Value.ShouldBe(nameof(LoginMode.External));
    }

    [Fact]
    public async Task Saving_sends_the_changed_settings_under_their_keys_and_leaves_the_app_configuration_alone()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Tabs(page).ShouldContain("Security"));
        page.Find("[data-testid='sign-in-save']").HasAttribute("disabled").ShouldBeTrue();

        Open(page, "Security");
        Input(page, "Sessions per user").Change("5");
        page.Find("[data-testid='sign-in-dirty']");
        await page.Find("[data-testid='sign-in-save']").ClickAsync(new());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global,
            Arg.Is<IReadOnlyDictionary<string, string?>>(c => c.Count == 1 && c[AuthSettings.MaxSessionsPerUser] == "5"), Arg.Any<CancellationToken>());
        await Api.DidNotReceiveWithAnyArgs().SaveAppConfigurationAsync(default!, default, default);
        page.WaitForAssertion(() => page.FindAll("[data-testid='sign-in-dirty']").ShouldBeEmpty());
    }

    [Fact]
    public async Task A_login_mode_and_a_switch_are_saved_as_their_settings()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => page.Find("[data-testid='sign-in-login-mode-toggle']"));

        await page.InvokeAsync(() => page.FindComponent<MudToggleGroup<string>>().Instance.ValueChanged.InvokeAsync(nameof(LoginMode.Internal)));
        Input(page, "Passkeys").Change(false);
        await page.Find("[data-testid='sign-in-save']").ClickAsync(new());

        await Api.Received(1).SetSettingsAsync(SettingScope.Global, Arg.Is<IReadOnlyDictionary<string, string?>>(c =>
            c.Count == 2 && c[AuthSettings.LoginMode] == nameof(LoginMode.Internal) && c[AuthSettings.PasskeysEnabled] == "false"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_registration_goes_back_inside_the_unchanged_app_settings()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Tabs(page).ShouldContain("Registration"));
        Open(page, "Registration");

        Input(page, "Ask for the address").Change(false);
        await page.InvokeAsync(() => page.FindComponent<MudExTagField<string>>().Instance.ValuesChanged.InvokeAsync(["*@acme.test", "*@beta.test"]));
        await page.Find("[data-testid='sign-in-document-add']").ClickAsync(new());
        page.Find("[data-testid='sign-in-document-slot'] input").Change("Passport");
        await page.Find("[data-testid='sign-in-save']").ClickAsync(new());

        await Api.Received(1).SaveAppConfigurationAsync(CoworkeeAppSettings.Section, Arg.Is<JsonElement>(v =>
            !v.GetProperty("registration").GetProperty("requireAddress").GetBoolean()
            && v.GetProperty("registration").GetProperty("allowedEmails").GetArrayLength() == 2
            && v.GetProperty("registration").GetProperty("documents")[0].GetProperty("name").GetString() == "Passport"
            && v.GetProperty("jobs").GetProperty("workerCount").GetInt32() == 7), Arg.Any<CancellationToken>());
        await Api.DidNotReceiveWithAnyArgs().SetSettingsAsync(default, default!, default);
        await Api.DidNotReceive().SaveAppConfigurationAsync(SignInDesign.Section, Arg.Any<JsonElement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Texts_are_saved_with_the_design_for_every_language_and_per_language()
    {
        Localization.GetLanguagesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new LanguageDto(null, "en", "English", true, true), new LanguageDto(null, "de", "Deutsch", true, false)]);
        await Services.GetRequiredService<Blazor.Localization.CoworkeeLocalizer>().RefreshAsync();
        var page = RenderPage();
        page.WaitForAssertion(() => Tabs(page).ShouldContain("Texts"));
        Open(page, "Texts");

        page.Find("input[data-testid='sign-in-text-title'], [data-testid='sign-in-text-title'] input").Change("Welcome aboard");
        await page.Find("[data-testid='sign-in-text-languages'] [data-culture='de']").ClickAsync(new());
        page.WaitForAssertion(() => page.Find("input[data-testid='sign-in-text-title'], [data-testid='sign-in-text-title'] input").GetAttribute("placeholder").ShouldBe("Welcome aboard"));
        page.Find("input[data-testid='sign-in-text-title'], [data-testid='sign-in-text-title'] input").Change("Willkommen");
        await page.Find("[data-testid='sign-in-save']").ClickAsync(new());

        await Api.Received(1).SaveAppConfigurationAsync(SignInDesign.Section, Arg.Is<JsonElement>(v =>
            v.GetProperty("texts").GetProperty("title").GetString() == "Welcome aboard"
            && v.GetProperty("texts").GetProperty("titles").GetProperty("de").GetString() == "Willkommen"
            && v.GetProperty("layout").GetString() == SignInLayouts.Card), Arg.Any<CancellationToken>());
        await Api.DidNotReceive().SaveAppConfigurationAsync(CoworkeeAppSettings.Section, Arg.Any<JsonElement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_design_card_and_the_texts_share_one_design_so_saving_one_keeps_the_other()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Tabs(page).ShouldContain("Appearance"));

        Open(page, "Appearance");
        await page.Find("[data-layout='Split']").ClickAsync(new());
        Open(page, "Texts");
        page.Find("textarea[data-testid='sign-in-text-notice'], [data-testid='sign-in-text-notice'] textarea").Change("Maintenance tonight");
        await page.Find("[data-testid='sign-in-save']").ClickAsync(new());

        await Api.Received(1).SaveAppConfigurationAsync(SignInDesign.Section, Arg.Is<JsonElement>(v =>
            v.GetProperty("layout").GetString() == SignInLayouts.Split && v.GetProperty("texts").GetProperty("notice").GetString() == "Maintenance tonight"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Discarding_restores_what_was_loaded()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Tabs(page).ShouldContain("Security"));
        Open(page, "Security");

        Input(page, "Sessions per user").Change("9");
        page.Find("[data-testid='sign-in-dirty']");
        page.Find("[data-testid='sign-in-discard']").Click();

        page.WaitForAssertion(() => Input(page, "Sessions per user").GetAttribute("value").ShouldBe("3"));
        page.FindAll("[data-testid='sign-in-dirty']").ShouldBeEmpty();
        Api.DidNotReceiveWithAnyArgs().SetSettingsAsync(default, default!, default);
    }

    [Fact]
    public void Locked_and_hidden_parts_of_the_registration_are_read_only_or_absent()
    {
        Api.GetAppConfigurationAsync(CoworkeeAppSettings.Section, Arg.Any<CancellationToken>()).Returns(AppSettings(new CoworkeeAppSettings(),
            locked: ["Registration:AllowedEmails"], hidden: ["Registration:Documents"]));

        var page = RenderPage();
        page.WaitForAssertion(() => Tabs(page).ShouldContain("Registration"));
        Open(page, "Registration");

        page.FindComponent<MudExTagField<string>>().Instance.Disabled.ShouldBeTrue();
        page.Find("[data-testid='sign-in-allowed-emails']").TextContent.ShouldContain("Set in the configuration");
        page.FindAll("[data-testid='sign-in-documents']").ShouldBeEmpty();
    }

    [Fact]
    public void Without_the_files_module_the_documents_are_disabled_with_a_hint()
    {
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([]);
        Api.GetPermissionDefinitionsAsync(Arg.Any<CancellationToken>()).Returns([new PermissionGroupDto("Settings", "Settings", [])]);

        var page = RenderPage();
        page.WaitForAssertion(() => Tabs(page).ShouldContain("Registration"));
        Open(page, "Registration");

        page.Find("[data-testid='sign-in-documents-unavailable']");
        page.Find("[data-testid='sign-in-document-add']").HasAttribute("disabled").ShouldBeTrue();
        page.Find("input[data-testid='sign-in-require-documents'], [data-testid='sign-in-require-documents'] input").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Another_organisation_gets_only_its_own_session_settings()
    {
        var page = RenderPage(system: false);

        page.WaitForAssertion(() => Tabs(page).ShouldBe(["Security"]));
        Labels(page).ShouldContain("Sessions per user");
        Labels(page).ShouldNotContain("Passkeys");
        Api.DidNotReceive().GetAppConfigurationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_appearance_stays_away_when_the_api_refuses_the_design()
    {
        Api.GetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>()).ThrowsAsync(new Api.ApiException(403, "configuration.system_only", null));

        var page = RenderPage();

        page.WaitForAssertion(() => Tabs(page).ShouldBe(["Methods", "Security", "Registration"]));
    }

    [Fact]
    public void The_appearance_stays_away_when_the_app_turns_the_sign_in_design_off()
    {
        Services.AddSingleton(new CoworkeeClientOptions { AllowSignInDesign = false });

        var page = RenderPage();

        page.WaitForAssertion(() => Tabs(page).ShouldBe(["Methods", "Security", "Registration"]));
        Api.DidNotReceive().GetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_design_saved_elsewhere_reloads_the_design_but_not_the_app_settings()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Tabs(page).ShouldContain("Appearance"));
        Open(page, "Appearance");
        Api.GetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>()).Returns(Design(new SignInDesign { Layout = SignInLayouts.Split, ShowAppName = false }));
        Api.ClearReceivedCalls();

        await page.InvokeAsync(() => Services.GetRequiredService<FakeRealtimeConnection>().Push(AppConfigurationChanges.Topic, RealtimeEventTypes.EntityChanged,
            new EntityChangedPayload("ConfigurationEntry", SignInDesign.Section + ":Layout", EntityChangedPayload.Updated, ["Value"])));

        page.WaitForAssertion(() => page.Find("[data-layout='Split']").GetAttribute("aria-checked").ShouldBe("true"));
        await Api.DidNotReceive().GetAppConfigurationAsync(CoworkeeAppSettings.Section, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_setting_saved_elsewhere_shows_unless_the_page_holds_own_changes()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Tabs(page).ShouldContain("Security"));
        Open(page, "Security");
        Api.GetSettingsAsync(SettingScope.Global, Arg.Any<CancellationToken>()).Returns([new SettingValueDto(AuthSettings.MaxSessionsPerUser, "4", true)]);

        await page.InvokeAsync(() => Services.GetRequiredService<FakeRealtimeConnection>().Push(SettingTopics.Global));
        page.WaitForAssertion(() => Input(page, "Sessions per user").GetAttribute("value").ShouldBe("4"));

        Input(page, "Sessions per user").Change("9");
        Api.GetSettingsAsync(SettingScope.Global, Arg.Any<CancellationToken>()).Returns([new SettingValueDto(AuthSettings.MaxSessionsPerUser, "1", true)]);
        await page.InvokeAsync(() => Services.GetRequiredService<FakeRealtimeConnection>().Push(SettingTopics.Global));
        Input(page, "Sessions per user").GetAttribute("value").ShouldBe("9");
    }

    [Fact]
    public async Task Restoring_the_design_defaults_resets_its_section_after_asking()
    {
        var dialogs = Render<MudDialogProvider>();
        Api.ResetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>()).Returns(Design(new SignInDesign()));
        var page = RenderPage();
        page.WaitForAssertion(() => Tabs(page).ShouldContain("Appearance"));
        Open(page, "Appearance");

        var resetting = page.InvokeAsync(() => page.Find("[data-testid='sign-in-reset']").ClickAsync(new()));
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Restore").ShouldBeTrue());
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Restore").ClickAsync(new());
        await resetting;

        await Api.Received(1).ResetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_save_bar_sits_in_the_sticky_tab_bar()
    {
        var page = RenderPage();

        page.WaitForAssertion(() => page.Find("[data-testid='sticky-tabs'] .mud-tabs-tabbar [data-testid='sign-in-save-bar']"));
    }

    [Theory]
    [InlineData("Coworkee:Registration:RequireAddress", "Coworkee", true)]
    [InlineData("Coworkee:Auth:Design:Layout", "Coworkee", false)]
    [InlineData("Coworkee:Auth:Design:Texts:Title", "Coworkee:Auth:Design", true)]
    [InlineData("Coworkee:Shop:Name", "Coworkee", false)]
    [InlineData("Other:Name", "Coworkee", false)]
    public void A_configuration_change_belongs_to_its_innermost_section(string key, string section, bool touches)
    {
        var envelope = new RealtimeEnvelope(AppConfigurationChanges.Topic, RealtimeEventTypes.EntityChanged,
            JsonSerializer.SerializeToElement(new EntityChangedPayload("ConfigurationEntry", key, EntityChangedPayload.Updated, ["Value"]), JsonSerializerOptions.Web));

        AppConfigurationChanges.Touches(envelope, section, [new ClientAppConfiguration("Coworkee:Shop", "Shop", typeof(object), null)]).ShouldBe(touches);
    }

    [Fact]
    public void The_page_and_its_menu_entry_need_the_settings_permission_and_the_system_organisation()
    {
        typeof(SignInConfiguration).GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(PermissionPolicy.Prefix + SettingsPermissions.Manage);
        var entry = new AdminNavigation().Items.Single(i => i.Href == "/admin/sign-in");
        entry.Permission.ShouldBe(SettingsPermissions.Manage);
        entry.HostOnly.ShouldBeTrue();
    }

    private IRenderedComponent<SignInConfiguration> RenderPage(bool system = true)
    {
        Render<MudPopoverProvider>();
        return Render<SignInConfiguration>(p => p.AddCascadingValue(Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(SystemTenant.ClaimType, system ? "true" : "false")], "test"))))));
    }

    private static List<string> Tabs(IRenderedComponent<SignInConfiguration> page) => [.. page.FindAll(".mud-tab").Select(t => t.TextContent.Trim())];

    private static void Open(IRenderedComponent<SignInConfiguration> page, string tab) => page.FindAll(".mud-tab").First(t => t.TextContent.Trim() == tab).Click();

    private void Definitions(bool loginMode = true, string? fixedMode = null)
    {
        List<SettingDefinitionDto> security =
        [
            new(PasswordSettings.RequiredLength, "Minimum password length", null, SettingType.Int, "8", Global, null),
            new(AuthSettings.PasskeysEnabled, "Passkeys", null, SettingType.Bool, "true", Global, null),
            new(AuthSettings.MaxSessionsPerUser, "Sessions per user", null, SettingType.Int, "0", Both, null),
            new("Account.ActiveRole", "Act within one role", null, SettingType.Bool, "true", Global, null),
        ];
        if (loginMode)
        {
            security.Insert(0, new(AuthSettings.LoginMode, "Sign-in methods", null, SettingType.Choice, "Both", Global, Enum.GetNames<LoginMode>(), FixedValue: fixedMode));
        }

        Api.GetSettingDefinitionsAsync(false, Arg.Any<CancellationToken>()).Returns(
        [
            new SettingGroupDto("Security", "Sign-in security", security),
            new SettingGroupDto("Account", "Accounts", [new("Account.AllowRegistration", "Allow self registration", null, SettingType.Bool, "false", Global, null)]),
            new SettingGroupDto("Cache", "Cache", [new("Cache.Minutes", "Cache minutes", null, SettingType.Int, "5", Global, null)]),
        ]);
    }

    private static AppConfigurationValuesDto AppSettings(CoworkeeAppSettings settings, IReadOnlyList<string>? locked = null, IReadOnlyList<string>? hidden = null) =>
        new(CoworkeeAppSettings.Section, JsonSerializer.SerializeToElement(settings, JsonSerializerOptions.Web), JsonSerializer.SerializeToElement(new CoworkeeAppSettings(), JsonSerializerOptions.Web), [],
            locked, hidden);

    private static AppConfigurationValuesDto Design(SignInDesign design) =>
        new(SignInDesign.Section, JsonSerializer.SerializeToElement(design, JsonSerializerOptions.Web), JsonSerializer.SerializeToElement(new SignInDesign(), JsonSerializerOptions.Web), []);
}
