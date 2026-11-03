using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

internal static class GroupNodes
{
    // the same answer the pipeline gives for a missing [RequiresPermission]
    internal static readonly Error NoBrowse = Error.Forbidden("auth.forbidden", $"Permission '{IdentityPermissions.Groups.View}' or '{IdentityPermissions.Groups.ScopeAll}' is required.");

    /// <summary>The tree routes: group admins, and operators with ScopeAll whose scope picker loads the tree lazily (no member lists).</summary>
    internal static async Task<bool> CanBrowseAsync(IPermissionChecker permissions, CancellationToken cancellationToken) =>
        await permissions.IsGrantedAsync(IdentityPermissions.Groups.View, cancellationToken)
        || await permissions.IsGrantedAsync(IdentityPermissions.Groups.ScopeAll, cancellationToken);

    internal static IQueryable<GroupNodeDto> ToNodes(this IQueryable<UserGroup> groups, CoworkeeDbContext db) => groups.AsNoTracking()
        .Select(g => new GroupNodeDto(g.Id, g.ParentId, g.Code ?? "", g.Name, g.Description,
            db.Set<UserGroup>().Count(c => c.ParentId == g.Id), g.Members.Count));
}
