using Coworkee.Contracts.Configuration;
using Coworkee.Settings;

namespace Coworkee.AuthServer;

internal static class LoginModes
{
    /// <summary>
    /// The sign-in methods in force: always the password form without a provider, so nobody gets locked out, otherwise the setting;
    /// a stored value that is no mode name falls back to the configured one.
    /// </summary>
    public static async Task<LoginMode> ResolveAsync(ISettingProvider settings, AuthServerOptions options, CancellationToken cancellationToken)
    {
        if (options.External.Providers.Count == 0)
        {
            return LoginMode.Internal;
        }

        var stored = await settings.GetAsync(AuthSettings.LoginMode, cancellationToken);
        return stored is not null && Enum.GetNames<LoginMode>().Contains(stored) ? Enum.Parse<LoginMode>(stored) : options.External.Mode;
    }
}
