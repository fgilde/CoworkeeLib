using Coworkee.Contracts.Configuration;
using Coworkee.Settings;

namespace Coworkee.Account;

internal sealed class SettingsSetupPasswordRules(ISettingProvider settings) : Application.Setup.ISetupPasswordRules
{
    public async Task<PasswordRules> GetAsync(CancellationToken cancellationToken) => PasswordRules.From(await settings.GetClientValuesAsync(cancellationToken));
}
