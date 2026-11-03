namespace Coworkee.Identity.Roles;

internal sealed class ActiveRoleAlwaysOn : IActiveRoleSwitch
{
    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}
