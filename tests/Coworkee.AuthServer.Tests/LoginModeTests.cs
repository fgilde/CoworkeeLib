using System.Net;
using Coworkee.Account;
using Coworkee.Contracts.Configuration;
using Coworkee.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer.Tests;

public sealed class LoginModeTests(AuthApp app) : IAsyncLifetime
{
    private const string PasswordForm = "data-sign-in=\"password\"";
    private const string ProviderButton = "Sign in with Keycloak";

    public async ValueTask InitializeAsync() => await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<string> LoginPageAsync() => app.Browser().GetStringAsync("/Account/Login", Ct);

    [Fact]
    public async Task Login_mode_setting_hides_the_password_form()
    {
        await app.SetSettingAsync(AuthSettings.LoginMode, "External");

        var page = await LoginPageAsync();

        page.ShouldContain(ProviderButton);
        page.ShouldNotContain(PasswordForm);
    }

    [Fact]
    public async Task The_internal_mode_hides_the_provider_buttons()
    {
        await app.SetSettingAsync(AuthSettings.LoginMode, "Internal");

        var page = await LoginPageAsync();

        page.ShouldContain(PasswordForm);
        page.ShouldNotContain(ProviderButton);
    }

    [Fact]
    public async Task A_stored_value_that_is_no_mode_falls_back_to_the_configured_one()
    {
        await app.SetSettingAsync(AuthSettings.LoginMode, "bogus");

        var page = await LoginPageAsync();

        page.ShouldContain(PasswordForm);
        page.ShouldContain(ProviderButton);
    }

    [Fact]
    public async Task Without_providers_the_password_form_always_shows()
    {
        await app.SetSettingAsync(AuthSettings.LoginMode, "External");
        var providers = app.App.Services.GetRequiredService<IOptions<AuthServerOptions>>().Value.External.Providers;
        var configured = providers.ToList();
        providers.Clear();
        try
        {
            var page = await LoginPageAsync();

            page.ShouldContain(PasswordForm);
            page.ShouldNotContain(ProviderButton);
        }
        finally
        {
            foreach (var (name, provider) in configured)
            {
                providers[name] = provider;
            }
        }
    }

    [Fact]
    public async Task The_operator_override_wins_over_the_setting()
    {
        await app.SetSettingAsync(AuthSettings.LoginMode, "External");
        var external = app.App.Services.GetRequiredService<IOptions<AuthServerOptions>>().Value.External;
        external.ModeOverride = LoginMode.Both;
        try
        {
            var page = await LoginPageAsync();

            page.ShouldContain(PasswordForm);
            page.ShouldContain(ProviderButton);
        }
        finally
        {
            external.ModeOverride = null;
        }
    }

    [Fact]
    public async Task A_provider_flow_started_before_switching_to_internal_does_not_sign_in()
    {
        using var browser = app.Browser();
        (await browser.GetAsync("/test/external?sub=kc-20&email=admin@acme.test", Ct)).EnsureSuccessStatusCode();
        await app.SetSettingAsync(AuthSettings.LoginMode, "Internal");

        using var callback = await browser.GetAsync("/Account/ExternalLogin?handler=Callback&returnUrl=%2Fhome", Ct);

        callback.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await browser.GetAsync("/Account/Manage/ChangePassword", Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Registration_closes_when_only_the_external_providers_sign_in()
    {
        await app.SetSettingAsync(AccountSettings.AllowRegistration, "true");
        await app.SetSettingAsync(AuthSettings.LoginMode, "External");

        (await app.Browser().GetAsync("/Account/Register", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Login_mode_is_defined_with_the_configured_mode_as_default_when_a_provider_is_configured()
    {
        var definition = app.App.Services.GetRequiredService<ISettingDefinitionManager>().Find(AuthSettings.LoginMode);

        definition.ShouldNotBeNull();
        definition.DefaultValue.ShouldBe("Both");
        definition.Choices.ShouldBe(["Internal", "External", "Both"]);
    }
}
