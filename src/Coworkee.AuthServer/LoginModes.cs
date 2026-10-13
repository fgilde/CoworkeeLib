using Coworkee.Contracts.Configuration;
using Coworkee.Settings;

namespace Coworkee.AuthServer;

internal static class LoginModes
{
    /// <summary>
    /// The sign-in methods in force: always the password form without a provider, so nobody gets locked out, otherwise the operator's
    /// override, else the setting; a missing definition or a stored value that is no mode name falls back to the configured mode.
    /// </summary>
    public static async Task<LoginMode> ResolveAsync(
        ISettingProvider settings, ISettingDefinitionManager definitions, AuthServerOptions options, CancellationToken cancellationToken)
    {
        if (options.External.Providers.Count == 0)
        {
            return LoginMode.Internal;
        }

        if (options.External.ModeOverride is { } forced)
        {
            return forced;
        }

        if (definitions.Find(AuthSettings.LoginMode) is null)
        {
            return options.External.Mode;
        }

        var stored = await settings.GetAsync(AuthSettings.LoginMode, cancellationToken);
        return stored is not null && Enum.GetNames<LoginMode>().Contains(stored) ? Enum.Parse<LoginMode>(stored) : options.External.Mode;
    }
}
