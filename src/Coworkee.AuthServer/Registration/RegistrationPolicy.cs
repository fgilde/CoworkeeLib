using Coworkee.Account;
using Coworkee.Settings;

namespace Coworkee.AuthServer.Registration;

/// <summary>The registration settings admins change at runtime.</summary>
public sealed record RegistrationPolicy(bool Enabled, bool RequiresActivation, bool RequiresEmailConfirmation)
{
    public static async Task<RegistrationPolicy> LoadAsync(ISettingProvider settings, CancellationToken cancellationToken) => new(
        await settings.GetAsync<bool>(AccountSettings.AllowRegistration, cancellationToken),
        await settings.GetAsync<bool>(AccountSettings.RegistrationRequiresActivation, cancellationToken),
        await settings.GetAsync<bool>(AccountSettings.RegistrationRequiresEmailConfirmation, cancellationToken));
}
