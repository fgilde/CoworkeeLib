using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Roles;

/// <summary>The roles the current user acts within: all of his roles, directly and through groups, or only the active one when the role switch is on.</summary>
public interface IActiveRoles
{
    Task<IReadOnlyCollection<Guid>> RoleIdsAsync(CancellationToken cancellationToken);
}

internal sealed class ActiveRoles(CoworkeeDbContext db, ICurrentUser currentUser, IActiveRoleSwitch roleSwitch) : IActiveRoles
{
    // once per user and tenant in a scope: resource restrictions ask for every checked resource
    private readonly Dictionary<(Guid, Guid?), IReadOnlyCollection<Guid>> _roleIds = [];

    public async Task<IReadOnlyCollection<Guid>> RoleIdsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return [];
        }

        var key = (userId, currentUser.TenantId);
        if (!_roleIds.TryGetValue(key, out var roles))
        {
            var all = await AllRoleIdsAsync(userId, cancellationToken);
            _roleIds[key] = roles = currentUser.PermissionScopes is null && await roleSwitch.IsEnabledAsync(cancellationToken) && await ActiveRoleIdAsync(userId, all, cancellationToken) is { } active
                ? [active]
                : all;
        }

        return roles;
    }

    internal Task<List<Guid>> RoleIdsAsync(Guid userId, List<Guid> groupIds, CancellationToken cancellationToken) =>
        db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == userId).Select(r => r.RoleId)
            .Union(db.Set<UserGroupRole>().Where(r => groupIds.Contains(r.GroupId)).Select(r => r.RoleId))
            .ToListAsync(cancellationToken);

    /// <summary>Every role of the user, directly and through groups, whatever role is active.</summary>
    internal async Task<List<Guid>> AllRoleIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await IsActiveMemberAsync(userId, currentUser.TenantId, cancellationToken)
            ? await RoleIdsAsync(userId, await GroupIdsAsync(userId, cancellationToken), cancellationToken)
            : [];

    /// <summary>The chosen role while the user still has it; a removed role falls back to all roles.</summary>
    internal async Task<Guid?> ActiveRoleIdAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        var value = await db.Set<IdentityUserClaim<Guid>>().Where(c => c.UserId == userId && c.ClaimType == ActiveRole.ClaimType)
            .Select(c => c.ClaimValue).FirstOrDefaultAsync(cancellationToken);
        return Guid.TryParse(value, out var roleId) && roleIds.Contains(roleId) ? roleId : null;
    }

    internal Task<bool> IsActiveMemberAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken, bool evenInactive = false) =>
        db.Set<User>().AnyAsync(u => u.Id == userId && u.TenantId == tenantId && (u.IsActive || evenInactive), cancellationToken);

    internal Task<List<Guid>> GroupIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        (from member in db.Set<UserGroupMember>()
         join grp in db.Set<UserGroup>() on member.GroupId equals grp.Id
         where member.UserId == userId
         select grp.Id).ToListAsync(cancellationToken);
}
