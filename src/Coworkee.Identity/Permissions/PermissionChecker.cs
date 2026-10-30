using Coworkee.Application.Authorization;
using Coworkee.Application.Caching;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Roles;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Permissions;

internal sealed class PermissionChecker(
    CoworkeeDbContext db, ICurrentUser currentUser, IPermissionDefinitionManager definitions, HybridCache cache, IEnumerable<IResourceHierarchy> hierarchies,
    IEnumerable<IResourceRestriction> restrictions, IActiveRoleSwitch activeRoles, IEnumerable<IComputedPermission> computed, IServiceProvider services)
    : IPermissionChecker
{
    private readonly IComputedPermission[] _computed = [.. computed];
    private readonly Dictionary<(Guid, Guid?, bool), IReadOnlyCollection<string>> _withComputed = [];

    public async Task<IReadOnlyCollection<string>> GetGrantedAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return currentUser.ClientId is null ? [] : await GetClientGrantedAsync(cancellationToken);
        }

        // an API key works within all roles of its user, whatever role the user picked in the app, and only within its scopes
        var scopes = currentUser.PermissionScopes;
        var narrow = scopes is null && await activeRoles.IsEnabledAsync(cancellationToken);
        var granted = await WithComputedAsync(userId, currentUser.TenantId, narrow, await GrantedAsync(userId, narrow, cancellationToken), cancellationToken);
        if (scopes is null)
        {
            return granted;
        }

        // a key stops working when its user may no longer have keys
        return granted.Contains(ApiKeyPermissions.Create) ? [.. granted.Where(definitions.Expand(scopes).Contains)] : [];
    }

    private async Task<IReadOnlyCollection<string>> GrantedAsync(Guid userId, bool narrow, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        return await cache.GetOrCreateInScopeAsync(
            Scopes,
            new ImpersonatedUser(userId, tenantId),
            $"coworkee:permissions:{tenantId}:{userId}:{(narrow ? "active" : "all")}",
            (scope, ct) => Own(scope).LoadAsync(userId, tenantId, narrow, ct),
            tags: [PermissionCache.Tag],
            cancellationToken: cancellationToken);
    }

    // a shared load reads through a checker of its own scope: the request that started it may end first (L64)
    private static PermissionChecker Own(IServiceProvider scope) => ActivatorUtilities.CreateInstance<PermissionChecker>(scope);

    private IServiceScopeFactory Scopes => services.GetRequiredService<IServiceScopeFactory>();

    private async Task<bool> InScopeAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        currentUser.PermissionScopes is not { } scopes
        || (definitions.Expand(scopes).Contains(permission) && (await GrantedAsync(userId, narrow: false, cancellationToken)).Contains(ApiKeyPermissions.Create));

    // a service client has what its roles in its organisation grant and what its token names
    private async Task<IReadOnlyCollection<string>> GetClientGrantedAsync(CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var roles = currentUser.Roles.Order(StringComparer.Ordinal).ToArray();
        var fromRoles = await cache.GetOrCreateInScopeAsync(
            Scopes,
            new ImpersonatedUser(null, tenantId),
            $"coworkee:permissions:roles:{tenantId}:{string.Join(',', roles)}",
            (scope, ct) => Own(scope).ClientRoleGrantsAsync(roles, tenantId, ct),
            tags: [PermissionCache.Tag],
            cancellationToken: cancellationToken);
        return [.. definitions.Expand(fromRoles.Concat(currentUser.ClientPermissions.Where(definitions.Exists)))];
    }

    private async Task<string[]> ClientRoleGrantsAsync(string[] roles, Guid? tenantId, CancellationToken cancellationToken)
    {
        var roleIds = await db.Set<Role>().Where(r => roles.Contains(r.Name!) && r.TenantId == tenantId && !r.IsSystem).Select(r => r.Id).ToListAsync(cancellationToken);
        return [.. await GrantsForRolesAsync(roleIds, tenantId, cancellationToken)];
    }

    public async Task<bool> IsGrantedAsync(string permission, CancellationToken cancellationToken) =>
        (await GetGrantedAsync(cancellationToken)).Contains(permission);

    public async Task<bool> IsGrantedAsync(string permission, string resourceType, Guid resourceId, CancellationToken cancellationToken) =>
        await IsGrantedOnResourceAsync(permission, resourceType, resourceId, cancellationToken) && !await IsRestrictedAsync(resourceType, resourceId, cancellationToken);

    private async Task<bool> IsRestrictedAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        var applicable = restrictions.Where(r => r.ResourceType == resourceType).ToList();
        if (applicable.Count == 0)
        {
            return false;
        }

        var roleIds = await GetRoleIdsAsync(cancellationToken);
        foreach (var restriction in applicable)
        {
            if (await restriction.IsRestrictedAsync(resourceId, roleIds, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> IsGrantedOnResourceAsync(string permission, string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        if (await IsGrantedAsync(permission, cancellationToken))
        {
            return true;
        }

        if (currentUser.UserId is not { } userId || !await InScopeAsync(userId, permission, cancellationToken) || !await IsActiveMemberAsync(userId, currentUser.TenantId, cancellationToken))
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
        if (currentUser.UserId is not { } userId || !await InScopeAsync(userId, permission, cancellationToken) || !await IsActiveMemberAsync(userId, currentUser.TenantId, cancellationToken))
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

    /// <summary>What <paramref name="userId"/> holds; <paramref name="narrow"/> false ignores the active role, so the result is everything his roles give.</summary>
    internal async Task<string[]> GetGrantedForAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken, bool narrow = true)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        narrow = narrow && await activeRoles.IsEnabledAsync(cancellationToken);
        return [.. await WithComputedAsync(userId, tenantId, narrow, await LoadAsync(userId, tenantId, narrow, cancellationToken), cancellationToken)];
    }

    /// <summary>Every permission <paramref name="userId"/> holds on a single resource, directly or through his groups (resource type, id, permission).</summary>
    internal async Task<HashSet<(string Type, Guid Id, string Permission)>> GetResourceGrantsForAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        var groupIds = await GroupIdsAsync(userId, cancellationToken);
        var grants = await db.Set<ResourcePermission>()
            .Where(p => (p.PrincipalType == PrincipalType.User && p.PrincipalId == userId)
                        || (p.PrincipalType == PrincipalType.Group && groupIds.Contains(p.PrincipalId)))
            .Select(p => new { p.ResourceType, p.ResourceId, p.RoleId })
            .Distinct()
            .ToListAsync(cancellationToken);
        var result = new HashSet<(string, Guid, string)>();
        foreach (var role in grants.GroupBy(g => g.RoleId))
        {
            var permissions = await GrantsForRolesAsync([role.Key], tenantId, cancellationToken);
            result.UnionWith(role.SelectMany(g => permissions.Select(p => (g.ResourceType, g.ResourceId, p))));
        }

        return result;
    }

    // ponytail: computed permissions run once per scope (request) and user, never across requests: data changes show on the next request without invalidation
    private async Task<IReadOnlyCollection<string>> WithComputedAsync(Guid userId, Guid? tenantId, bool narrow, IReadOnlyCollection<string> granted, CancellationToken cancellationToken)
    {
        if (_computed.Length == 0)
        {
            return granted;
        }

        var key = (userId, tenantId, narrow);
        if (_withComputed.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var extra = new List<string>();
        if (await IsActiveMemberAsync(userId, tenantId, cancellationToken))
        {
            var context = new ComputedPermissionContext(userId, tenantId, granted, services);
            foreach (var permission in _computed.Where(c => !granted.Contains(c.Permission)))
            {
                if (!definitions.Exists(permission.Permission))
                {
                    throw new InvalidOperationException($"Computed permission '{permission.Permission}' is not defined.");
                }

                if (await permission.IsGrantedAsync(context, cancellationToken))
                {
                    extra.Add(permission.Permission);
                }
            }
        }

        return _withComputed[key] = extra.Count == 0 ? granted : [.. definitions.Expand(granted.Concat(extra))];
    }

    // once per user and tenant in a scope: resource restrictions ask for every checked resource
    private readonly Dictionary<(Guid, Guid?), IReadOnlyCollection<Guid>> _roleIds = [];

    public async Task<IReadOnlyCollection<Guid>> GetRoleIdsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return [];
        }

        var key = (userId, currentUser.TenantId);
        if (!_roleIds.TryGetValue(key, out var roles))
        {
            var all = await AllRoleIdsAsync(userId, cancellationToken);
            _roleIds[key] = roles = currentUser.PermissionScopes is null && await activeRoles.IsEnabledAsync(cancellationToken) && await ActiveRoleIdAsync(userId, all, cancellationToken) is { } active
                ? [active]
                : all;
        }

        return roles;
    }

    private Task<List<Guid>> RoleIdsAsync(Guid userId, List<Guid> groupIds, CancellationToken cancellationToken) =>
        db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == userId).Select(r => r.RoleId)
            .Union(db.Set<UserGroupRole>().Where(r => groupIds.Contains(r.GroupId)).Select(r => r.RoleId))
            .ToListAsync(cancellationToken);

    /// <summary>Every role of the user, directly and through groups, whatever role is active.</summary>
    internal async Task<List<Guid>> AllRoleIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await IsActiveMemberAsync(userId, currentUser.TenantId, cancellationToken)
            ? await RoleIdsAsync(userId, await GroupIdsAsync(userId, cancellationToken), cancellationToken)
            : [];

    internal Task<IReadOnlySet<string>> RoleGrantsAsync(Guid roleId, CancellationToken cancellationToken) =>
        GrantsForRolesAsync([roleId], currentUser.TenantId, cancellationToken);

    /// <summary>The chosen role while the user still has it; a removed role falls back to all roles.</summary>
    internal async Task<Guid?> ActiveRoleIdAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        var value = await db.Set<IdentityUserClaim<Guid>>().Where(c => c.UserId == userId && c.ClaimType == ActiveRole.ClaimType)
            .Select(c => c.ClaimValue).FirstOrDefaultAsync(cancellationToken);
        return Guid.TryParse(value, out var roleId) && roleIds.Contains(roleId) ? roleId : null;
    }

    // the active role narrows to that role alone, without direct grants, so it never adds to what the roles give
    private async Task<string[]> LoadAsync(Guid userId, Guid? tenantId, bool narrow, CancellationToken cancellationToken)
    {
        if (!await IsActiveMemberAsync(userId, tenantId, cancellationToken))
        {
            return [];
        }

        var groupIds = await GroupIdsAsync(userId, cancellationToken);
        var roleIds = await RoleIdsAsync(userId, groupIds, cancellationToken);
        if (roleIds.Count > 1 && narrow && await ActiveRoleIdAsync(userId, roleIds, cancellationToken) is { } active)
        {
            return [.. definitions.Expand(await GrantsForRolesAsync([active], tenantId, cancellationToken))];
        }

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

    // ponytail: the role, group and direct grant ids are loaded into lists; one query with subqueries when a tenant has many thousands of grants
    public async Task<Contracts.PagedResult<Guid>> UsersGrantedAsync(string permission, Guid? groupId, Contracts.PageRequest page, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var names = definitions.All.Select(d => d.Name).Where(n => definitions.Expand([n]).Contains(permission)).Append(permission).Distinct(StringComparer.Ordinal).ToList();
        var grants = db.Set<PermissionGrant>().Where(g => names.Contains(g.Name) && (g.TenantId == null || g.TenantId == tenantId));
        var roleIds = await grants.Where(g => g.ProviderType == PermissionProviderType.Role).Select(g => g.ProviderKey).ToListAsync(cancellationToken);
        roleIds.AddRange(await db.Set<Role>().Where(r => r.IsSystem && r.Name == SystemRoles.Admin).Select(r => r.Id).ToListAsync(cancellationToken));
        var direct = await grants.Where(g => g.ProviderType == PermissionProviderType.User).Select(g => g.ProviderKey).ToListAsync(cancellationToken);
        var groupIds = await db.Set<UserGroup>()
            .Where(g => grants.Any(x => x.ProviderType == PermissionProviderType.Group && x.ProviderKey == g.Id)
                        || db.Set<UserGroupRole>().Any(r => r.GroupId == g.Id && roleIds.Contains(r.RoleId)))
            .Select(g => g.Id).ToListAsync(cancellationToken);

        var users = db.Set<User>().AsNoTracking().Where(u => u.TenantId == tenantId && u.IsActive)
            .Where(u => direct.Contains(u.Id)
                        || db.Set<IdentityUserRole<Guid>>().Any(r => r.UserId == u.Id && roleIds.Contains(r.RoleId))
                        || db.Set<UserGroupMember>().Any(m => m.UserId == u.Id && groupIds.Contains(m.GroupId)));
        if (groupId is { } group)
        {
            var code = await db.Set<UserGroup>().Where(g => g.Id == group).Select(g => g.Code).SingleOrDefaultAsync(cancellationToken);
            var subtree = db.Set<UserGroup>().Where(g => g.Id == group || (code != null && g.Code!.StartsWith(code + "."))).Select(g => g.Id);
            users = users.Where(u => db.Set<UserGroupMember>().Any(m => m.UserId == u.Id && subtree.Contains(m.GroupId)));
        }

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var search = page.Search.Trim().ToLowerInvariant();
            users = users.Where(u => u.Email!.ToLower().Contains(search)
                                     || (u.FirstName ?? string.Empty).ToLower().Contains(search)
                                     || (u.LastName ?? string.Empty).ToLower().Contains(search));
        }

        var total = await users.CountAsync(cancellationToken);
        var ids = await users.OrderBy(u => u.Email).ThenBy(u => u.Id).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).Select(u => u.Id).ToListAsync(cancellationToken);
        return new Contracts.PagedResult<Guid>(ids, total, page.Page, page.PageSize);
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
