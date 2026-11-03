namespace Coworkee.Identity.Roles;

/// <summary>Whether users with several roles may act within one of them; the account module ties it to a system setting.</summary>
public interface IActiveRoleSwitch
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);
}
