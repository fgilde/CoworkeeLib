using Coworkee.Contracts.Configuration;
using Coworkee.Settings;

namespace Coworkee.AuthServer;

/// <summary>Passkeys belong to the own sign-in: off by the setting or when only the external provider signs in (it brings its own).</summary>
internal static class Passkeys
{
    public const string Section = $"{AuthServerOptions.Section}:Passkeys";

    public const int MaxPerUser = 20;

    public static async Task<bool> EnabledAsync(LoginMode mode, ISettingProvider settings, CancellationToken cancellationToken) =>
        mode != LoginMode.External && await settings.GetAsync<bool>(AuthSettings.PasskeysEnabled, cancellationToken);

    public static async Task<bool> EnabledAsync(
        ISettingProvider settings, ISettingDefinitionManager definitions, AuthServerOptions options, CancellationToken cancellationToken) =>
        await EnabledAsync(await LoginModes.ResolveAsync(settings, definitions, options, cancellationToken), settings, cancellationToken);
}
