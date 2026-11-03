using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Permissions;

internal sealed class ResourceAccessReader(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionDefinitionManager definitions) : IResourceAccessReader
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetPrincipalsAsync(
        string permission, string resourceType, IReadOnlyCollection<Guid> resourceIds, CancellationToken cancellationToken)
    {
        var ids = resourceIds.Distinct().ToList();
        var grants = await db.Set<ResourcePermission>().AsNoTracking().Where(p => p.ResourceType == resourceType && ids.Contains(p.ResourceId))
            .Select(p => new { p.ResourceId, p.PrincipalType, p.PrincipalId, p.RoleId }).ToListAsync(cancellationToken);
        var roles = await RolesGrantingAsync(permission, grants.Select(g => g.RoleId).Distinct().ToList(), cancellationToken);

        // inactive users stay listed: callers resolve the current user at read time, so reactivation needs no refresh
        return grants.Where(g => roles.Contains(g.RoleId))
            .GroupBy(g => g.ResourceId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(p => p.PrincipalType == PrincipalType.User ? PrincipalKeys.User(p.PrincipalId) : PrincipalKeys.Group(p.PrincipalId)).Distinct().ToList());
    }

    public async Task<IReadOnlyList<string>> GetCurrentPrincipalsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || (await ActiveUsersAsync([userId], cancellationToken)).Count == 0)
        {
            return [];
        }

        var groups = await (from member in db.Set<UserGroupMember>()
                            join grp in db.Set<UserGroup>() on member.GroupId equals grp.Id
                            where member.UserId == userId
                            select grp.Id).ToListAsync(cancellationToken);
        return [PrincipalKeys.User(userId), .. groups.Select(PrincipalKeys.Group)];
    }

    private async Task<HashSet<Guid>> RolesGrantingAsync(string permission, List<Guid> roleIds, CancellationToken cancellationToken)
    {
        var allowed = (await db.Set<Role>().Where(r => roleIds.Contains(r.Id) && r.IsSystem && r.Name == SystemRoles.Admin).Select(r => r.Id).ToListAsync(cancellationToken)).ToHashSet();
        var tenantId = currentUser.TenantId;
        var grants = await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == PermissionProviderType.Role && roleIds.Contains(g.ProviderKey) && (g.TenantId == null || g.TenantId == tenantId))
            .Select(g => new { g.ProviderKey, g.Name }).ToListAsync(cancellationToken);
        foreach (var role in grants.GroupBy(g => g.ProviderKey))
        {
            if (definitions.Expand(role.Select(g => g.Name)).Contains(permission))
            {
                allowed.Add(role.Key);
            }
        }

        return allowed;
    }

    private async Task<HashSet<Guid>> ActiveUsersAsync(List<Guid> userIds, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        return (await db.Set<User>().Where(u => userIds.Contains(u.Id) && u.TenantId == tenantId && u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken)).ToHashSet();
    }
}
