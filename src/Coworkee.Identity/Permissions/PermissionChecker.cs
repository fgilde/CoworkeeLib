using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Coworkee.Identity.Permissions;

internal sealed class PermissionChecker(
    CoworkeeDbContext db, ICurrentUser currentUser, IPermissionDefinitionManager definitions, HybridCache cache, IEnumerable<IResourceHierarchy> hierarchies)
    : IPermissionChecker
{
    public async Task<IReadOnlyCollection<string>> GetGrantedAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return [];
        }

        var tenantId = currentUser.TenantId;
        return await cache.GetOrCreateAsync(
            $"coworkee:permissions:{tenantId}:{userId}",
            async ct =>
            {
                using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
                return await LoadAsync(userId, tenantId, ct);
            },
            tags: [PermissionCache.Tag],
            cancellationToken: cancellationToken);
    }

    public async Task<bool> IsGrantedAsync(string permission, CancellationToken cancellationToken) =>
        (await GetGrantedAsync(cancellationToken)).Contains(permission);

    public async Task<bool> IsGrantedAsync(string permission, string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        if (await IsGrantedAsync(permission, cancellationToken))
        {
            return true;
        }

        if (currentUser.UserId is not { } userId || !await IsActiveMemberAsync(userId, currentUser.TenantId, cancellationToken))
        {
            return false;
        }

        var groupIds = await GroupIdsAsync(userId, cancellationToken);
        var chain = hierarchies.FirstOrDefault(h => h.ResourceType == resourceType) is { } hierarchy
            ? await hierarchy.GetInheritanceChainAsync(resourceId, cancellationToken)
            : [resourceId];
        var roleIds = await db.Set<ResourcePermission>()
            .Where(p => p.ResourceType == resourceType && chain.Contains(p.ResourceId))
            .Where(p => (p.PrincipalType == PrincipalType.User && p.PrincipalId == userId)
                        || (p.PrincipalType == PrincipalType.Group && groupIds.Contains(p.PrincipalId)))
            .Select(p => p.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return roleIds.Count > 0 && (await GrantsForRolesAsync(roleIds, currentUser.TenantId, cancellationToken)).Contains(permission);
    }

    public async Task<IReadOnlyCollection<Guid>> GetGrantedResourcesAsync(string permission, string resourceType, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || !await IsActiveMemberAsync(userId, currentUser.TenantId, cancellationToken))
        {
            return [];
        }

        var groupIds = await GroupIdsAsync(userId, cancellationToken);
        var grants = await db.Set<ResourcePermission>()
            .Where(p => p.ResourceType == resourceType)
            .Where(p => (p.PrincipalType == PrincipalType.User && p.PrincipalId == userId)
                        || (p.PrincipalType == PrincipalType.Group && groupIds.Contains(p.PrincipalId)))
            .Select(p => new { p.ResourceId, p.RoleId })
            .ToListAsync(cancellationToken);
        var allowedRoles = new HashSet<Guid>();
        foreach (var roleId in grants.Select(g => g.RoleId).Distinct())
        {
            if ((await GrantsForRolesAsync([roleId], currentUser.TenantId, cancellationToken)).Contains(permission))
            {
                allowedRoles.Add(roleId);
            }
        }

        return grants.Where(g => allowedRoles.Contains(g.RoleId)).Select(g => g.ResourceId).Distinct().ToList();
    }

    internal async Task<string[]> GetGrantedForAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        return await LoadAsync(userId, tenantId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Guid>> GetRoleIdsAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId && await IsActiveMemberAsync(userId, currentUser.TenantId, cancellationToken)
            ? await RoleIdsAsync(userId, await GroupIdsAsync(userId, cancellationToken), cancellationToken)
            : [];

    private Task<List<Guid>> RoleIdsAsync(Guid userId, List<Guid> groupIds, CancellationToken cancellationToken) =>
        db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == userId).Select(r => r.RoleId)
            .Union(db.Set<UserGroupRole>().Where(r => groupIds.Contains(r.GroupId)).Select(r => r.RoleId))
            .ToListAsync(cancellationToken);

    private async Task<string[]> LoadAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken)
    {
        if (!await IsActiveMemberAsync(userId, tenantId, cancellationToken))
        {
            return [];
        }

        var groupIds = await GroupIdsAsync(userId, cancellationToken);
        var roleIds = await RoleIdsAsync(userId, groupIds, cancellationToken);

        var roleGrants = await GrantsForRolesAsync(roleIds, tenantId, cancellationToken);
        var direct = await db.Set<PermissionGrant>()
            .Where(g => g.TenantId == null || g.TenantId == tenantId)
            .Where(g => (g.ProviderType == PermissionProviderType.User && g.ProviderKey == userId)
                        || (g.ProviderType == PermissionProviderType.Group && groupIds.Contains(g.ProviderKey)))
            .Select(g => g.Name)
            .ToListAsync(cancellationToken);

        return [.. definitions.Expand(roleGrants.Concat(direct))];
    }

    private async Task<IReadOnlySet<string>> GrantsForRolesAsync(List<Guid> roleIds, Guid? tenantId, CancellationToken cancellationToken)
    {
        if (await db.Set<Role>().AnyAsync(r => roleIds.Contains(r.Id) && r.IsSystem && r.Name == SystemRoles.Admin, cancellationToken))
        {
            return definitions.Expand(definitions.All.Select(d => d.Name));
        }

        var names = await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == PermissionProviderType.Role && roleIds.Contains(g.ProviderKey))
            .Where(g => g.TenantId == null || g.TenantId == tenantId)
            .Select(g => g.Name)
            .ToListAsync(cancellationToken);
        return definitions.Expand(names);
    }

    private Task<bool> IsActiveMemberAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken) =>
        db.Set<User>().AnyAsync(u => u.Id == userId && u.TenantId == tenantId && u.IsActive, cancellationToken);

    private Task<List<Guid>> GroupIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        (from member in db.Set<UserGroupMember>()
         join grp in db.Set<UserGroup>() on member.GroupId equals grp.Id
         where member.UserId == userId
         select grp.Id).ToListAsync(cancellationToken);
}

// ponytail: any grant/role/group change invalidates every cached permission set; per-user tags when write volume matters
public sealed class PermissionCache(HybridCache cache)
{
    internal const string Tag = "coworkee:permissions";

    public Task InvalidateAsync(CancellationToken cancellationToken) => cache.RemoveByTagAsync(Tag, cancellationToken).AsTask();
}
