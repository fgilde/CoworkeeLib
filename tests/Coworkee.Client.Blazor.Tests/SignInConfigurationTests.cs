using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
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
        JSInterop.Setup<MudBlazor.Extensions.Core.CssVariable[]>("MudExCssHelper.getCssVariables", _ => true).SetResult([]);
        Services.AddSingleton(new ClientAppConfiguration(CoworkeeAppSettings.Section, "App settings", typeof(CoworkeeAppSettings), null, IsAppSettings: true));
    }

    [Fact]
    public void Gathers_the_sign_in_settings_the_registration_part_of_the_app_settings_and_the_sign_in_design()
    {
        var page = RenderPage();

        page.WaitForAssertion(() => Labels(page).ShouldContain("Sign-in methods"));
        Labels(page).ShouldContain("Passkeys");
        Labels(page).ShouldContain("Sessions per user");
        Labels(page).ShouldContain("Minimum password length");
        Labels(page).ShouldContain("Allow self registration");
        Labels(page).ShouldNotContain("Cache minutes");
        Input(page, "Sessions per user").GetAttribute("value").ShouldBe("3");

        // the registration of the app settings, nothing else of them
        page.WaitForAssertion(() => page.Find("[data-testid='sign-in-registration']").InnerHtml.ShouldContain("*@acme.test"));
        Labels(page).ShouldContain(nameof(RegistrationOptions.RequireAddress));
        Labels(page).ShouldContain(nameof(RegistrationOptions.Documents));
        page.FindAll("[data-testid='sign-in-registration'] input").Select(i => i.GetAttribute("value")).ShouldNotContain("7");

        page.Find("[data-testid='sign-in-design']");
        page.WaitForAssertion(() => Labels(page).ShouldContain(nameof(SignInDesign.ShowAppName)));
        Labels(page).ShouldContain("Title");
        Labels(page).ShouldContain("Notice");
        Labels(page).ShouldNotContain("Layout");
    }

    [Fact]
    public void Without_an_external_provider_the_sign_in_methods_do_not_show()
    {
        Definitions(loginMode: false);

        var page = RenderPage();

        page.WaitForAssertion(() => Labels(page).ShouldContain("Passkeys"));
        Labels(page).ShouldNotContain("Sign-in methods");
    }

    [Fact]
    public void A_mode_the_host_fixes_shows_read_only_with_a_hint()
    {
        Definitions(fixedMode: nameof(LoginMode.Both));

        var page = RenderPage();

        page.WaitForAssertion(() => page.Markup.ShouldContain("The host fixes Both in its configuration; the stored value applies once it no longer does."));
        page.FindAll(".mud-input-control.mud-disabled, .mud-disabled").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Saves_settings_as_settings_and_the_registration_and_texts_as_app_configuration()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Input(page, "Sessions per user"));
        page.WaitForAssertion(() => page.FindComponents<AppConfigurationEditor<SignInDesign>>().ShouldHaveSingleItem());
        page.WaitForAssertion(() => page.FindComponents<AppConfigurationEditor<CoworkeeAppSettings>>().ShouldHaveSingleItem());

        Input(page, "Sessions per user").Change("5");
        await page.InvokeAsync(() => page.FindComponent<SettingsForm>().Instance.SaveAsync());
        await Api.Received(1).SetSettingsAsync(SettingScope.Global,
            Arg.Is<IReadOnlyDictionary<string, string?>>(c => c.Count == 1 && c[AuthSettings.MaxSessionsPerUser] == "5"), Arg.Any<CancellationToken>());

        // the other app settings go back unchanged
        await page.InvokeAsync(() => page.FindComponent<AppConfigurationEditor<CoworkeeAppSettings>>().Instance.SaveAsync());
        await Api.Received(1).SaveAppConfigurationAsync(CoworkeeAppSettings.Section, Arg.Is<JsonElement>(v =>
            v.GetProperty("registration").GetProperty("requireAddress").GetBoolean() && v.GetProperty("jobs").GetProperty("workerCount").GetInt32() == 7), Arg.Any<CancellationToken>());

        Input(page, "Title").Change("Welcome aboard");
        await page.InvokeAsync(() => page.FindComponent<AppConfigurationEditor<SignInDesign>>().Instance.SaveAsync());
        await Api.Received(1).SaveAppConfigurationAsync(SignInDesign.Section, Arg.Is<JsonElement>(v =>
            v.GetProperty("texts").GetProperty("title").GetString() == "Welcome aboard" && v.GetProperty("layout").GetString() == SignInLayouts.Card), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Another_organisation_gets_its_own_session_settings_and_no_app_configuration()
    {
        var page = RenderPage(system: false);

        page.WaitForAssertion(() => Labels(page).ShouldContain("Sessions per user"));
        Labels(page).ShouldNotContain("Passkeys");
        page.FindAll("[data-testid='sign-in-registration']").ShouldBeEmpty();
        page.FindAll("[data-testid='sign-in-appearance']").ShouldBeEmpty();
        Api.DidNotReceive().GetAppConfigurationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_appearance_stays_away_when_the_app_turns_the_sign_in_design_off()
    {
        Services.AddSingleton(new CoworkeeClientOptions { AllowSignInDesign = false });

        var page = RenderPage();

        page.WaitForAssertion(() => page.Find("[data-testid='sign-in-registration']"));
        page.FindAll("[data-testid='sign-in-appearance']").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_design_saved_elsewhere_reloads_the_design_but_not_the_app_settings()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => page.FindComponents<AppConfigurationEditor<SignInDesign>>().ShouldHaveSingleItem());
        page.WaitForAssertion(() => page.FindComponents<AppConfigurationEditor<CoworkeeAppSettings>>().ShouldHaveSingleItem());
        Api.GetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>()).Returns(Design(new SignInDesign { Layout = SignInLayouts.Split, ShowAppName = false }));
        Api.ClearReceivedCalls();

        await page.InvokeAsync(() => Services.GetRequiredService<FakeRealtimeConnection>().Push(AppConfigurationChanges.Topic, RealtimeEventTypes.EntityChanged,
            new EntityChangedPayload("ConfigurationEntry", SignInDesign.Section + ":Layout", EntityChangedPayload.Updated, ["Value"])));

        page.WaitForAssertion(() => page.Find("[data-layout='Split']").GetAttribute("aria-checked").ShouldBe("true"));
        page.WaitForAssertion(() => Input(page, nameof(SignInDesign.ShowAppName)).HasAttribute("checked").ShouldBeFalse());
        await Api.DidNotReceive().GetAppConfigurationAsync(CoworkeeAppSettings.Section, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_setting_saved_elsewhere_shows_unless_the_form_holds_own_changes()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Input(page, "Sessions per user").GetAttribute("value").ShouldBe("3"));
        Api.GetSettingsAsync(SettingScope.Global, Arg.Any<CancellationToken>()).Returns([new SettingValueDto(AuthSettings.MaxSessionsPerUser, "4", true)]);

        await page.InvokeAsync(() => Services.GetRequiredService<FakeRealtimeConnection>().Push(SettingTopics.Global));
        page.WaitForAssertion(() => Input(page, "Sessions per user").GetAttribute("value").ShouldBe("4"));

        Input(page, "Sessions per user").Change("9");
        Api.GetSettingsAsync(SettingScope.Global, Arg.Any<CancellationToken>()).Returns([new SettingValueDto(AuthSettings.MaxSessionsPerUser, "1", true)]);
        await page.InvokeAsync(() => Services.GetRequiredService<FakeRealtimeConnection>().Push(SettingTopics.Global));
        Input(page, "Sessions per user").GetAttribute("value").ShouldBe("9");
    }

    [Fact]
    public async Task The_design_card_shows_a_thumbnail_per_layout_and_saves_the_installation_wide_design()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => page.FindAll("[data-testid='sign-in-layout']").Count.ShouldBe(SignInLayouts.All.Count));
        page.Find("[data-layout='Card']").GetAttribute("aria-checked").ShouldBe("true");

        await page.Find("[data-layout='Split']").ClickAsync(new());
        page.Find("input[data-testid='sign-in-media'], [data-testid='sign-in-media'] input").Change("/media/welcome.jpg");
        await page.Find("[data-testid='sign-in-save']").ClickAsync(new());

        await Api.Received(1).SaveAppConfigurationAsync(SignInDesign.Section,
            Arg.Is<JsonElement>(v => v.GetProperty("layout").GetString() == SignInLayouts.Split && v.GetProperty("mediaUrl").GetString() == "/media/welcome.jpg"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_design_card_stays_hidden_when_the_user_may_not_change_the_app_configuration()
    {
        Api.GetAppConfigurationAsync(SignInDesign.Section, Arg.Any<CancellationToken>()).ThrowsAsync(new Api.ApiException(403, "configuration.system_only", null));

        var page = RenderPage();

        page.WaitForAssertion(() => page.Find("[data-testid='sign-in-registration']"));
        page.FindAll("[data-testid='sign-in-design']").ShouldBeEmpty();
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

    private void Definitions(bool loginMode = true, string? fixedMode = null)
    {
        List<SettingDefinitionDto> security =
        [
            new(PasswordSettings.RequiredLength, "Minimum password length", null, SettingType.Int, "8", Global, null),
            new(AuthSettings.PasskeysEnabled, "Passkeys", null, SettingType.Bool, "true", Global, null),
            new(AuthSettings.MaxSessionsPerUser, "Sessions per user", null, SettingType.Int, "0", Both, null),
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

    private static AppConfigurationValuesDto AppSettings(CoworkeeAppSettings settings) =>
        new(CoworkeeAppSettings.Section, JsonSerializer.SerializeToElement(settings, JsonSerializerOptions.Web), JsonSerializer.SerializeToElement(new CoworkeeAppSettings(), JsonSerializerOptions.Web), []);

    private static AppConfigurationValuesDto Design(SignInDesign design) =>
        new(SignInDesign.Section, JsonSerializer.SerializeToElement(design, JsonSerializerOptions.Web), JsonSerializer.SerializeToElement(new SignInDesign(), JsonSerializerOptions.Web), []);
}
