using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Users;

/// <summary>
/// The one rule for acting on other accounts and handing out permissions. Nobody changes or controls the account of a user who holds a permission he
/// lacks (a resource grant counts unless the caller holds the permission globally or on that resource: the superset rule of impersonation), only administrators act on administrators, and nobody grants or revokes a permission he does not hold
/// (implied permissions expanded) or the administrator role without being an administrator. The caller counts with what he holds right now (his active
/// role, the scopes of his API key, an app's decorated checker); the target with everything all his roles give. Administrators hold every permission, so
/// they pass for everyone of their organisation.
/// </summary>
public sealed class AccountGuard(
    CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker checker, IPermissionDefinitionManager definitions, IServiceProvider services)
{
    public static readonly Error MorePermissions = Error.Forbidden("identity.user_more_permissions", "The user holds permissions you do not have.");

    public static readonly Error AdminOnly = Error.Forbidden("identity.admin_role_restricted", "Only administrators can change the account of an administrator.");

    public static readonly Error GrantRestricted = Error.Forbidden("identity.grant_restricted", "You can only grant or revoke permissions you hold yourself.");

    // the lib's own checker for other users' sets, also when the app decorates IPermissionChecker
    private PermissionChecker Permissions => _permissions ??= checker as PermissionChecker ?? ActivatorUtilities.CreateInstance<PermissionChecker>(services);

    private PermissionChecker? _permissions;

    /// <summary>
    /// Null when the caller may change the account of <paramref name="userId"/>, a user of his organisation; his own account always. With group-scoped
    /// administration the user must be in his reach (else 404) with all his groups (<see cref="AdminReach.CheckUserAsync"/>).
    /// </summary>
    public Task<Error?> CheckUserAsync(Guid userId, CancellationToken cancellationToken) => CheckUserAsync(userId, false, cancellationToken);

    /// <summary><see cref="CheckUserAsync(Guid, CancellationToken)"/>; <paramref name="joining"/>: the user joins a group of the caller, so a user of no group passes the reach.</summary>
    internal async Task<Error?> CheckUserAsync(Guid userId, bool joining, CancellationToken cancellationToken)
    {
        if (userId == currentUser.UserId)
        {
            return null;
        }

        if (await services.GetRequiredService<AdminReach>().CheckUserAsync(userId, cancellationToken, joining) is { } outOfReach)
        {
            return outOfReach;
        }

        if (!await CallerIsAdminAsync(cancellationToken) && await AdminGuard.IsAdminAsync(db, userId, cancellationToken))
        {
            return AdminOnly;
        }

        return await ExceedsAsync(await MineAsync(cancellationToken), g => checker.IsGrantedAsync(g.Permission, g.Type, g.Id, cancellationToken), userId, currentUser.TenantId, cancellationToken)
            ? MorePermissions
            : null;
    }

    /// <summary><see cref="CheckUserAsync"/> for each of <paramref name="userIds"/>; the first refusal.</summary>
    // ponytail: one permission load per user; batch the loads if member changes of thousands of users at once get slow
    public Task<Error?> CheckUsersAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken) => CheckUsersAsync(userIds, false, cancellationToken);

    internal async Task<Error?> CheckUsersAsync(IEnumerable<Guid> userIds, bool joining, CancellationToken cancellationToken)
    {
        // the answer for each user is yes then: no permission load per user when an administrator moves thousands
        if (await HoldsEverythingAsync(cancellationToken))
        {
            return null;
        }

        foreach (var userId in userIds.Distinct())
        {
            if (await CheckUserAsync(userId, joining, cancellationToken) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    /// <summary>Null when the caller holds every one of <paramref name="names"/> and what they imply, so he may grant or revoke them.</summary>
    public async Task<Error?> CheckGrantAsync(IEnumerable<string> names, CancellationToken cancellationToken)
    {
        var mine = await MineAsync(cancellationToken);
        return definitions.Expand(names).All(mine.Contains) ? null : GrantRestricted;
    }

    /// <summary>
    /// Null when the caller may give or take <paramref name="roleIds"/>: the administrator role needs an administrator, any other role every permission
    /// it grants. Default roles pass: every account a user manager creates gets them anyway, and only who held their permissions made them default.
    /// </summary>
    public async Task<Error?> CheckRolesAsync(IEnumerable<Guid> roleIds, CancellationToken cancellationToken)
    {
        var ids = roleIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return null;
        }

        var roles = await db.Set<Role>().AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.IsSystem, r.Name, r.IsDefault }).ToListAsync(cancellationToken);
        if (roles.Any(r => r.IsSystem && r.Name == SystemRoles.Admin) && !await CallerIsAdminAsync(cancellationToken))
        {
            return AdminGuard.AdminRoleRestricted;
        }

        var names = new List<string>();
        foreach (var role in roles.Where(r => !r.IsDefault))
        {
            names.AddRange(await Permissions.RoleGrantsAsync(role.Id, cancellationToken));
        }

        return await CheckGrantAsync(names, cancellationToken);
    }

    /// <summary>
    /// Null when the caller may add users to or remove them from <paramref name="groupIds"/>: he holds what membership gives (the groups' grants, roles and
    /// resource grants). The default group passes, as new users join it anyway.
    /// </summary>
    public async Task<Error?> CheckGroupsAsync(IEnumerable<Guid> groupIds, CancellationToken cancellationToken)
    {
        var wanted = groupIds.Distinct().ToList();
        var ids = wanted.Count == 0 ? [] : await db.Set<UserGroup>().Where(g => wanted.Contains(g.Id) && !g.IsDefault).Select(g => g.Id).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return null;
        }

        var roleIds = await db.Set<UserGroupRole>().Where(r => ids.Contains(r.GroupId)).Select(r => r.RoleId).ToListAsync(cancellationToken);
        if (await CheckRolesAsync(roleIds, cancellationToken) is { } roles)
        {
            return roles;
        }

        var grants = await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == PermissionProviderType.Group && ids.Contains(g.ProviderKey) && (g.TenantId == null || g.TenantId == currentUser.TenantId))
            .Select(g => g.Name).ToListAsync(cancellationToken);
        if (await CheckGrantAsync(grants, cancellationToken) is { } granted)
        {
            return granted;
        }

        var onResources = await db.Set<ResourcePermission>().Where(p => p.PrincipalType == PrincipalType.Group && ids.Contains(p.PrincipalId))
            .Select(p => new { p.ResourceType, p.ResourceId, p.RoleId }).Distinct().ToListAsync(cancellationToken);
        foreach (var grant in onResources)
        {
            if (await CheckResourceGrantAsync(grant.RoleId, grant.ResourceType, grant.ResourceId, cancellationToken) is { } restricted)
            {
                return restricted;
            }
        }

        return null;
    }

    /// <summary>Null when the caller holds every permission of <paramref name="roleId"/>, globally or on the resource, so he may grant or revoke it there.</summary>
    public async Task<Error?> CheckResourceGrantAsync(Guid roleId, string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        var mine = await MineAsync(cancellationToken);
        foreach (var permission in await Permissions.RoleGrantsAsync(roleId, cancellationToken))
        {
            if (!mine.Contains(permission) && !await checker.IsGrantedAsync(permission, resourceType, resourceId, cancellationToken))
            {
                return GrantRestricted;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="targetId"/> holds a permission outside <paramref name="mine"/> (all his roles, also while deactivated) or a resource grant
    /// neither covered by <paramref name="mine"/> nor held on that resource (<paramref name="holdsOnResource"/>).
    /// </summary>
    internal async Task<bool> ExceedsAsync(
        IReadOnlyCollection<string> mine, Func<(string Type, Guid Id, string Permission), Task<bool>> holdsOnResource, Guid targetId, Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var theirs = await Permissions.GetGrantedForAsync(targetId, tenantId, cancellationToken, narrow: false, evenInactive: true);
        if (theirs.Any(p => !mine.Contains(p)))
        {
            return true;
        }

        foreach (var grant in await Permissions.GetResourceGrantsForAsync(targetId, tenantId, cancellationToken))
        {
            if (!mine.Contains(grant.Permission) && !await holdsOnResource(grant))
            {
                return true;
            }
        }

        return false;
    }

    // the system (ISystemDispatcher, CurrentUserScope.BeginSystem) acts for app code that decided the caller may cause the change: it passes as an administrator
    private static bool IsSystem => CurrentUserScope.Current is SystemActor;

    private async Task<HashSet<string>> MineAsync(CancellationToken cancellationToken) =>
        IsSystem ? [.. definitions.All.Select(d => d.Name)] : [.. await checker.GetGrantedAsync(cancellationToken)];

    // an administrator of the whole organisation who holds every permission right now (no narrower active role or key scopes)
    private async Task<bool> HoldsEverythingAsync(CancellationToken cancellationToken)
    {
        if (IsSystem)
        {
            return true;
        }

        if (!await CallerIsAdminAsync(cancellationToken) || !await services.GetRequiredService<AdminReach>().AllAsync(cancellationToken))
        {
            return false;
        }

        var mine = await MineAsync(cancellationToken);
        return definitions.All.All(d => mine.Contains(d.Name));
    }

    private async Task<bool> CallerIsAdminAsync(CancellationToken cancellationToken) =>
        IsSystem || (currentUser.UserId is { } me && await AdminGuard.IsAdminAsync(db, me, cancellationToken));
}
