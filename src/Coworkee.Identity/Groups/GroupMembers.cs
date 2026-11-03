using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

internal static class GroupMembers
{
    public static readonly Error UnknownUser = Error.Validation("UserIds", "Unknown user.");

    /// <summary>Users of the tenant the caller may name for his group (<see cref="Users.AdminReach.FilterJoinableAsync"/>); the others are unknown.</summary>
    public static async Task<bool> AllInTenantAsync(CoworkeeDbContext db, ICurrentUser currentUser, Users.AdminReach reach, List<Guid> userIds, CancellationToken ct) =>
        await (await reach.FilterJoinableAsync(db.Set<User>(), ct)).CountAsync(u => EF.Parameter(userIds).Contains(u.Id) && u.TenantId == currentUser.TenantId, ct)
        == userIds.Count;

    public static void AddMissing(UserGroup group, IEnumerable<Guid> userIds) =>
        group.Members.AddRange(userIds.Where(id => group.Members.All(m => m.UserId != id)).Select(id => new UserGroupMember { GroupId = group.Id, UserId = id }));

    /// <summary>
    /// Joining or leaving groups changes the users' accounts and gives or takes what the groups grant (<see cref="Users.AccountGuard"/>); a user of no
    /// group may join one of the caller's groups.
    /// </summary>
    public static async Task<Error?> CheckAsync(Users.AccountGuard guard, IEnumerable<Guid> groupIds, IEnumerable<Guid> joining, IEnumerable<Guid> leaving, CancellationToken ct)
    {
        var (join, leave) = (joining.ToList(), leaving.ToList());
        return join.Count + leave.Count == 0 ? null
            : await guard.CheckUsersAsync(join, true, ct) ?? await guard.CheckUsersAsync(leave, ct) ?? await guard.CheckGroupsAsync(groupIds, ct);
    }

    /// <summary>The group with its members; null when it does not exist or lies out of the caller's reach.</summary>
    public static async Task<UserGroup?> LoadAsync(CoworkeeDbContext db, Users.AdminReach reach, Guid id, CancellationToken ct) =>
        await reach.ReachesGroupAsync(id, ct) ? await db.Set<UserGroup>().Include(g => g.Members).SingleOrDefaultAsync(g => g.Id == id, ct) : null;
}
