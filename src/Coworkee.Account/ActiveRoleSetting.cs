using Coworkee.Identity.Roles;
using Coworkee.Settings;

namespace Coworkee.Account;

/// <summary>The system setting behind <see cref="IActiveRoleSwitch"/>; on by default.</summary>
internal sealed class ActiveRoleSetting(ISettingProvider settings) : IActiveRoleSwitch
{
    public const string Name = "Account.ActiveRole";

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => settings.GetAsync<bool>(Name, cancellationToken);
}
