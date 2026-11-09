using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Roles;

/// <summary>
/// The rules of <see cref="SetUserRolesHandler"/> for every user it takes: the account rule (<see cref="AccountGuard.CheckUsersAsync(IEnumerable{Guid}, CancellationToken)"/>),
/// the roles given and, moved, taken (<see cref="AccountGuard.CheckRolesAsync"/>) and the last administrator. Rows go through the change tracker, so the
/// audit trail, realtime messages and the permission cache follow as for single changes.
/// </summary>
internal sealed class TransferRoleUsersHandler(CoworkeeDbContext db, ICurrentUser currentUser, AccountGuard guard, AdminReach reach) : IHandler<TransferRoleUsers, Result<int>>
{
    public async Task<Result<int>> HandleAsync(TransferRoleUsers command, CancellationToken cancellationToken)
    {
        var (source, request) = (command.RoleId, command.Request);
        var targets = request.TargetRoleIds.Distinct().ToList();
        var roles = targets.Append(source).ToList();
        if (await db.Set<Role>().CountAsync(r => roles.Contains(r.Id) && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken) != roles.Count)
        {
            return RoleNames.NotFound;
        }

        // users out of the caller's reach are unknown to him (AdminReach): they keep their roles
        var users = await reach.FilterUsersAsync(db.Set<User>().Where(u => u.TenantId == currentUser.TenantId), cancellationToken);
        var members = users.Where(u => db.Set<IdentityUserRole<Guid>>().Any(r => r.UserId == u.Id && r.RoleId == source)).Select(u => u.Id);
        if (request.UserIds is { } chosen)
        {
            var wanted = chosen.Distinct().ToList();
            members = members.Where(id => wanted.Contains(id));
        }

        var userIds = await members.ToListAsync(cancellationToken);
        if (request.UserIds is { } named && userIds.Count != named.Distinct().Count())
        {
            return Error.Validation(nameof(request.UserIds), "Unknown user or the user does not have the role.");
        }

        if (userIds.Count == 0)
        {
            return 0;
        }

        if ((await guard.CheckUsersAsync(userIds, cancellationToken) ?? await guard.CheckRolesAsync(request.Move ? roles : targets, cancellationToken)) is { } refused)
        {
            return refused;
        }

        if (request.Move && source == await AdminGuard.AdminRoleIdAsync(db, cancellationToken)
            && !await AdminGuard.OtherActiveAdminExistsAsync(db, currentUser.TenantId, userIds, cancellationToken))
        {
            return AdminGuard.LastAdmin;
        }

        // the queries name the members again instead of sending thousands of ids
        var had = (await db.Set<IdentityUserRole<Guid>>().Where(r => targets.Contains(r.RoleId) && members.Contains(r.UserId))
            .Select(r => new { r.UserId, r.RoleId }).ToListAsync(cancellationToken)).Select(r => (r.UserId, r.RoleId)).ToHashSet();
        db.AddRange(from user in userIds
                    from role in targets
                    where !had.Contains((user, role))
                    select new IdentityUserRole<Guid> { UserId = user, RoleId = role });
        if (request.Move)
        {
            db.RemoveRange(await db.Set<IdentityUserRole<Guid>>().Where(r => r.RoleId == source && members.Contains(r.UserId)).ToListAsync(cancellationToken));
        }

        return userIds.Count;
    }
}
