using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;
using NSubstitute;

namespace Coworkee.AuthServer.Tests;

public sealed class LoginModeResolutionTests
{
    private static readonly SettingDefinition Definition = new(AuthSettings.LoginMode, "Security", "Sign-in methods", SettingType.Choice, [SettingScope.Global], "Both", false, null, null);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<LoginMode> ResolveAsync(string? stored, bool defined, LoginMode? forced, bool providers = true, LoginMode configured = LoginMode.Both)
    {
        var settings = Substitute.For<ISettingProvider>();
        settings.GetAsync(AuthSettings.LoginMode, Arg.Any<CancellationToken>()).Returns(stored);
        var definitions = Substitute.For<ISettingDefinitionManager>();
        definitions.Find(AuthSettings.LoginMode).Returns(defined ? Definition : null);
        var options = new AuthServerOptions { External = { Mode = configured, ModeOverride = forced } };
        if (providers)
        {
            options.External.Providers["keycloak"] = new ExternalProviderOptions();
        }

        return LoginModes.ResolveAsync(settings, definitions, options, Ct);
    }

    [Fact]
    public async Task The_override_wins_over_the_stored_setting() =>
        (await ResolveAsync("External", defined: true, forced: LoginMode.Both)).ShouldBe(LoginMode.Both);

    [Fact]
    public async Task Without_an_override_the_stored_setting_counts() =>
        (await ResolveAsync("External", defined: true, forced: null)).ShouldBe(LoginMode.External);

    [Fact]
    public async Task Without_providers_the_override_does_not_matter() =>
        (await ResolveAsync("External", defined: true, forced: LoginMode.External, providers: false)).ShouldBe(LoginMode.Internal);

    [Fact]
    public async Task A_missing_definition_falls_back_to_the_configured_mode() =>
        (await ResolveAsync(null, defined: false, forced: null, configured: LoginMode.External)).ShouldBe(LoginMode.External);
}
