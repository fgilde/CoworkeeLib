using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Groups;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

/// <summary>Who may sign in as whom; checked when the ticket is issued, when the auth server redeems it and on every authorize and refresh of the impersonation.</summary>
public sealed class ImpersonationRules(CoworkeeDbContext db, IPermissionChecker checker, AccountGuard guard, TimeProvider clock)
{
    internal static readonly Error Forbidden = Error.Forbidden("identity.impersonate_forbidden", "You may not sign in as other users.");

    private static readonly Error Self = Error.Conflict("identity.impersonate_self", "You are signed in as yourself already.");

    private static readonly Error Already = Error.Conflict("identity.already_impersonating", "Return to your own account before you sign in as another user.");

    private static readonly Error MorePermissions = Error.Forbidden("identity.impersonate_more_permissions", "The user holds permissions you do not have.");

    private static readonly Error OtherGroups = Error.Forbidden("identity.impersonate_other_groups", "The user belongs to groups you cannot access.");

    /// <summary>
    /// Null when <paramref name="impersonatorId"/> may sign in as <paramref name="targetId"/>: he holds the permission and is not signed in as someone else
    /// (<paramref name="currentImpersonator"/>), the target is another active, unlocked user of the tenant, holds no permission he lacks (all roles,
    /// whatever role either has picked; resource grants only where he holds the permission globally or on the same resource) and belongs only to groups
    /// he can access.
    /// </summary>
    public async Task<Error?> CheckAsync(Guid impersonatorId, Guid? currentImpersonator, Guid targetId, Guid? tenantId, CancellationToken cancellationToken)
    {
        if (currentImpersonator is not null)
        {
            return Already;
        }

        if (targetId == impersonatorId)
        {
            return Self;
        }

        bool exists;
        var now = clock.GetUtcNow();
        using (CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId)))
        {
            exists = await db.Set<User>().AnyAsync(u => u.Id == targetId && u.TenantId == tenantId && u.IsActive && !(u.LockoutEnabled && u.LockoutEnd > now), cancellationToken);
        }

        if (!exists)
        {
            return UserErrors.NotFound;
        }

        // the full sets: the target can switch his active role, so a narrowed comparison would let him hold more than the impersonator
        var permissions = (PermissionChecker)checker;
        var mine = await permissions.GetGrantedForAsync(impersonatorId, tenantId, cancellationToken, narrow: false);
        if (!mine.Contains(IdentityPermissions.Users.Impersonate))
        {
            return Forbidden;
        }

        // the superset rule every change of another account follows (AccountGuard)
        var myResources = await permissions.GetResourceGrantsForAsync(impersonatorId, tenantId, cancellationToken);
        if (await guard.ExceedsAsync(mine, g => Task.FromResult(myResources.Contains(g)), targetId, tenantId, cancellationToken))
        {
            return MorePermissions;
        }

        return mine.Contains(IdentityPermissions.Groups.ScopeAll) || await InAccessibleGroupsAsync(impersonatorId, targetId, tenantId, cancellationToken) ? null : OtherGroups;
    }

    // accessible as IGroupScope computes it: the impersonator's groups and their subtrees, by code
    private async Task<bool> InAccessibleGroupsAsync(Guid impersonatorId, Guid targetId, Guid? tenantId, CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        // a join, not g.Members.Where(...): SQLite has no APPLY
        var groups = await (from member in db.Set<UserGroupMember>()
                            where member.UserId == impersonatorId || member.UserId == targetId
                            join g in db.Set<UserGroup>() on member.GroupId equals g.Id
                            select new { member.UserId, g.Id, g.Code }).AsNoTracking().ToListAsync(cancellationToken);
        var mine = groups.Where(g => g.UserId == impersonatorId).ToList();
        return groups.Where(g => g.UserId == targetId).All(target => mine.Any(own =>
            own.Id == target.Id || (own.Code is { } code && target.Code is { } targetCode && GroupCodes.IsDescendant(targetCode, code))));
    }
}
