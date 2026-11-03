using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Roles;

/// <summary>
/// What new users get (a default role, a role offered in the registration, the roles of the default group) goes to people nobody vetted:
/// only who may manage users and holds every permission of it (as for impersonation) hands it out, also by granting such a role more.
/// </summary>
internal sealed class RoleOffer(IPermissionChecker permissions, CoworkeeDbContext db, ICurrentUser currentUser, Users.AccountGuard guard)
{
    public static readonly Error MorePermissions = Error.Forbidden("identity.role_offer_more_permissions",
        "Only a user who may manage users and holds every permission of the role can give it to new users or offer it in the registration.");

    /// <summary>Refused unless the caller has <c>Users.Manage</c> and every permission the roles grant, plus <paramref name="grants"/>.</summary>
    public async Task<Error?> RefusedAsync(IReadOnlyCollection<Guid> roleIds, IEnumerable<string>? grants, CancellationToken cancellationToken)
    {
        if (!await permissions.IsGrantedAsync(IdentityPermissions.Users.Manage, cancellationToken))
        {
            return MorePermissions;
        }

        var ids = roleIds.ToList();
        var names = ids.Count == 0 ? [] : await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == PermissionProviderType.Role && EF.Parameter(ids).Contains(g.ProviderKey) && (g.TenantId == null || g.TenantId == currentUser.TenantId))
            .Select(g => g.Name).ToListAsync(cancellationToken);
        return await guard.CheckGrantAsync(names.Concat(grants ?? []), cancellationToken) is null ? null : MorePermissions;
    }

    /// <summary>The role is a default role, offered in the registration or a role of the tenant's default group.</summary>
    public async Task<bool> ReachesNewUsersAsync(Guid roleId, CancellationToken cancellationToken) =>
        await db.Set<Role>().AnyAsync(r => r.Id == roleId && (r.IsDefault || r.SelectableForRegistration), cancellationToken)
        || await db.Set<UserGroup>().AnyAsync(g => g.IsDefault && g.Roles.Any(r => r.RoleId == roleId), cancellationToken);
}
