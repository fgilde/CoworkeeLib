using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

[RequiresPermission(IdentityPermissions.Groups.ManageMembers)]
public sealed record AddGroupMembers(Guid Id, IReadOnlyList<Guid> UserIds) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Groups.ManageMembers)]
public sealed record RemoveGroupMembers(Guid Id, IReadOnlyList<Guid> UserIds) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Groups.ManageMembers)]
public sealed record MoveGroupMembers(Guid FromId, Guid ToId) : ICommand<Result>;

/// <summary>Makes exactly these users the owners of the group; users who are not members yet join it.</summary>
[RequiresPermission(IdentityPermissions.Groups.ManageMembers)]
public sealed record SetGroupOwners(Guid Id, IReadOnlyList<Guid> UserIds) : ICommand<Result>;

internal sealed class SetGroupOwnersValidator : AbstractValidator<SetGroupOwners>
{
    public SetGroupOwnersValidator() => RuleFor(c => c.UserIds).NotNull();
}

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

internal sealed class AddGroupMembersHandler(CoworkeeDbContext db, ICurrentUser currentUser, Users.AccountGuard guard, Users.AdminReach reach) : IHandler<AddGroupMembers, Result>
{
    public async Task<Result> HandleAsync(AddGroupMembers command, CancellationToken cancellationToken)
    {
        var group = await GroupMembers.LoadAsync(db, reach, command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var wanted = command.UserIds.Distinct().ToList();
        if (!await GroupMembers.AllInTenantAsync(db, currentUser, reach, wanted, cancellationToken))
        {
            return GroupMembers.UnknownUser;
        }

        if (await GroupMembers.CheckAsync(guard, [group.Id], wanted.Where(id => group.Members.All(m => m.UserId != id)), [], cancellationToken) is { } refused)
        {
            return refused;
        }

        GroupMembers.AddMissing(group, wanted);
        return Result.Success();
    }
}

internal sealed class RemoveGroupMembersHandler(CoworkeeDbContext db, Users.AccountGuard guard, Users.AdminReach reach) : IHandler<RemoveGroupMembers, Result>
{
    public async Task<Result> HandleAsync(RemoveGroupMembers command, CancellationToken cancellationToken)
    {
        var group = await GroupMembers.LoadAsync(db, reach, command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var leaving = group.Members.Where(m => command.UserIds.Contains(m.UserId)).Select(m => m.UserId);
        if (await GroupMembers.CheckAsync(guard, [group.Id], [], leaving, cancellationToken) is { } refused)
        {
            return refused;
        }

        group.Members.RemoveAll(m => command.UserIds.Contains(m.UserId));
        return Result.Success();
    }
}

internal sealed class MoveGroupMembersHandler(CoworkeeDbContext db, Users.AccountGuard guard, Users.AdminReach reach) : IHandler<MoveGroupMembers, Result>
{
    public async Task<Result> HandleAsync(MoveGroupMembers command, CancellationToken cancellationToken)
    {
        var from = await GroupMembers.LoadAsync(db, reach, command.FromId, cancellationToken);
        var to = command.ToId == command.FromId ? from : await GroupMembers.LoadAsync(db, reach, command.ToId, cancellationToken);
        if (from is null || to is null)
        {
            return GroupErrors.NotFound;
        }

        if (from != to)
        {
            var moved = from.Members.Select(m => m.UserId).ToList();
            if (await GroupMembers.CheckAsync(guard, [from.Id, to.Id], [], moved, cancellationToken) is { } refused)
            {
                return refused;
            }

            from.Members.Clear();
            GroupMembers.AddMissing(to, moved);
        }

        return Result.Success();
    }
}

internal sealed class SetGroupOwnersHandler(CoworkeeDbContext db, ICurrentUser currentUser, Users.AccountGuard guard, Users.AdminReach reach) : IHandler<SetGroupOwners, Result>
{
    public async Task<Result> HandleAsync(SetGroupOwners command, CancellationToken cancellationToken)
    {
        var group = await GroupMembers.LoadAsync(db, reach, command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var wanted = command.UserIds.Distinct().ToList();
        if (!await GroupMembers.AllInTenantAsync(db, currentUser, reach, wanted, cancellationToken))
        {
            return GroupMembers.UnknownUser;
        }

        if (await GroupMembers.CheckAsync(guard, [group.Id], wanted.Where(id => group.Members.All(m => m.UserId != id)), [], cancellationToken) is { } refused)
        {
            return refused;
        }

        GroupMembers.AddMissing(group, wanted);
        foreach (var member in group.Members)
        {
            member.IsOwner = wanted.Contains(member.UserId);
        }

        return Result.Success();
    }
}
