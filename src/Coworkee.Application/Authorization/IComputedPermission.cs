namespace Coworkee.Application.Authorization;

/// <summary>A permission granted from data instead of a role ("leads someone"); evaluated once per request and user, after the role grants.</summary>
public interface IComputedPermission
{
    /// <summary>A defined permission (it implies what its definition implies).</summary>
    string Permission { get; }

    Task<bool> IsGrantedAsync(ComputedPermissionContext context, CancellationToken cancellationToken);
}
