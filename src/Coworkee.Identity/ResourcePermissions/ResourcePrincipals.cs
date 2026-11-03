using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.ResourcePermissions;

internal static class ResourcePrincipals
{
    // ponytail: one membership query per user grant; a resource with hundreds of user grants shown to a group-scoped administrator wants one query
    public static async Task<bool> ReachesAsync(Users.AdminReach reach, PrincipalType type, Guid id, CancellationToken cancellationToken) =>
        type == PrincipalType.User ? await reach.ReachesUserAsync(id, cancellationToken) : await reach.ReachesGroupAsync(id, cancellationToken);
}
