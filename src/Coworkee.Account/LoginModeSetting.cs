using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;
using Microsoft.Extensions.Configuration;

namespace Coworkee.Account;

/// <summary>
/// Reads the configuration instead of the options because the host of the settings API need not be the auth server; there
/// it takes the mode and the display names of the providers, never their secrets.
/// </summary>
internal sealed class LoginModeSettingDefinitions(IConfiguration configuration) : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context)
    {
        var external = configuration.GetSection($"{AuthServerOptions.Section}:{nameof(AuthServerOptions.External)}").Get<ExternalLoginOptions>();
        if (external is not { Providers.Count: > 0 })
        {
            return;
        }

        context.Group("Security", "Sign-in security")
            .Add(AuthSettings.LoginMode, "Sign-in methods", SettingType.Choice, [SettingScope.Global], external.Mode.ToString(),
                description: "Password, external providers (e.g. Keycloak) or both. Shows only when an external provider is configured.",
                choices: Enum.GetNames<LoginMode>());
    }
}
