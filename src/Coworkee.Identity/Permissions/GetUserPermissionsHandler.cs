using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Roles;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Permissions;

/// <summary>
/// Reads the sources the way <see cref="PermissionChecker"/> combines them: own roles, roles of his groups, grants to his groups and to him, each expanded by what
/// a permission implies; whatever the checker adds beyond those is computed.
/// </summary>
internal sealed class GetUserPermissionsHandler(
    CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker checker, IPermissionDefinitionManager definitions, Users.AdminReach reach)
    : IHandler<GetUserPermissions, Result<UserPermissionsDto>>
{
    public async Task<Result<UserPermissionsDto>> HandleAsync(GetUserPermissions query, CancellationToken cancellationToken)
    {
        var (userId, tenantId) = (query.UserId, currentUser.TenantId);
        if (!await db.Set<User>().AnyAsync(u => u.Id == userId && u.TenantId == tenantId, cancellationToken) || !await reach.ReachesUserAsync(userId, cancellationToken))
        {
            return Error.NotFound("identity.user_not_found", "The user does not exist.");
        }

        var groups = await (from member in db.Set<UserGroupMember>()
                            join grp in db.Set<UserGroup>() on member.GroupId equals grp.Id
                            where member.UserId == userId
                            select new { grp.Id, grp.Name }).ToListAsync(cancellationToken);
        var groupIds = groups.Select(g => g.Id).ToList();
        // two queries: a set operation over a constant null column does not translate on every provider
        var roles = await (from userRole in db.Set<IdentityUserRole<Guid>>()
                           join role in db.Set<Role>() on userRole.RoleId equals role.Id
                           where userRole.UserId == userId
                           select new RoleSource(role.Id, role.Name!, role.IsSystem, null)).ToListAsync(cancellationToken);
        roles.AddRange(await (from groupRole in db.Set<UserGroupRole>()
                              join role in db.Set<Role>() on groupRole.RoleId equals role.Id
                              where groupIds.Contains(groupRole.GroupId)
                              select new RoleSource(role.Id, role.Name!, role.IsSystem, groupRole.GroupId)).ToListAsync(cancellationToken));
        var roleIds = roles.Select(r => r.Id).Distinct().ToList();
        var grants = await db.Set<PermissionGrant>()
            .Where(g => g.TenantId == null || g.TenantId == tenantId)
            .Where(g => (g.ProviderType == PermissionProviderType.User && g.ProviderKey == userId)
                        || (g.ProviderType == PermissionProviderType.Group && groupIds.Contains(g.ProviderKey))
                        || (g.ProviderType == PermissionProviderType.Role && roleIds.Contains(g.ProviderKey)))
            .Select(g => new { g.ProviderType, g.ProviderKey, g.Name })
            .ToListAsync(cancellationToken);

        var sources = new Dictionary<string, List<PermissionSourceDto>>(StringComparer.Ordinal);
        void Add(IEnumerable<string> names, PermissionSourceDto source)
        {
            foreach (var name in definitions.Expand(names))
            {
                (sources.TryGetValue(name, out var list) ? list : sources[name] = []).Add(source);
            }
        }

        string GroupName(Guid? id) => groups.FirstOrDefault(g => g.Id == id)?.Name ?? string.Empty;
        IEnumerable<string> Granted(PermissionProviderType type, Guid key) => grants.Where(g => g.ProviderType == type && g.ProviderKey == key).Select(g => g.Name);

        foreach (var role in roles.Distinct())
        {
            var names = role.IsSystem && role.Name == SystemRoles.Admin ? definitions.All.Select(d => d.Name) : Granted(PermissionProviderType.Role, role.Id);
            Add(names, new PermissionSourceDto(PermissionSourceKind.Role, role.Id, role.Name, role.GroupId, role.GroupId is null ? null : GroupName(role.GroupId)));
        }

        foreach (var group in groups)
        {
            Add(Granted(PermissionProviderType.Group, group.Id), new PermissionSourceDto(PermissionSourceKind.Group, group.Id, group.Name));
        }

        var direct = Granted(PermissionProviderType.User, userId).Order(StringComparer.Ordinal).ToList();
        Add(direct, new PermissionSourceDto(PermissionSourceKind.Direct));

        // what the checker gives on top (computed permissions); also what a deactivated user would hold once active again
        var held = await ((PermissionChecker)checker).GetGrantedForAsync(userId, tenantId, cancellationToken, narrow: false, evenInactive: true);
        foreach (var name in held.Where(n => !sources.ContainsKey(n)))
        {
            sources[name] = [new PermissionSourceDto(PermissionSourceKind.Computed)];
        }

        return new UserPermissionsDto(direct, [.. sources.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => new UserPermissionDto(s.Key, s.Value))]);
    }

    private sealed record RoleSource(Guid Id, string Name, bool IsSystem, Guid? GroupId);
}
