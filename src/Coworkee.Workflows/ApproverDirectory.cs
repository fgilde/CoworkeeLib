using Coworkee.Application.Authorization;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Workflows;

/// <summary>Roles by name: who has them in the organisation and whether the current user has one.</summary>
public sealed class ApproverDirectory(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker permissions)
{
    public async Task<IReadOnlyList<Guid>> UsersInRoleAsync(IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        if (roles.Count == 0)
        {
            return [];
        }

        var tenantId = currentUser.TenantId;
        var roleIds = RoleIds(roles);
        var direct = db.Set<IdentityUserRole<Guid>>().Where(r => roleIds.Contains(r.RoleId)).Select(r => r.UserId);
        var viaGroups = db.Set<UserGroupMember>()
            .Where(m => db.Set<UserGroupRole>().Any(g => g.GroupId == m.GroupId && roleIds.Contains(g.RoleId)))
            .Select(m => m.UserId);
        return await db.Set<User>().IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.IsActive && (direct.Contains(u.Id) || viaGroups.Contains(u.Id)))
            .Select(u => u.Id).ToListAsync(cancellationToken);
    }

    public async Task<bool> CurrentUserHasRoleAsync(IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        var mine = await permissions.GetRoleIdsAsync(cancellationToken);
        return mine.Count > 0 && await RoleIds(roles).AnyAsync(id => mine.Contains(id), cancellationToken);
    }

    private IQueryable<Guid> RoleIds(IReadOnlyCollection<string> roles)
    {
        var tenantId = currentUser.TenantId;
        return db.Set<Role>().IgnoreQueryFilters().Where(r => roles.Contains(r.Name!) && (r.TenantId == null || r.TenantId == tenantId)).Select(r => r.Id);
    }
}
