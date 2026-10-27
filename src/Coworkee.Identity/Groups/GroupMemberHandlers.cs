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

    public static async Task<bool> AllInTenantAsync(CoworkeeDbContext db, ICurrentUser currentUser, List<Guid> userIds, CancellationToken ct) =>
        await db.Set<User>().CountAsync(u => EF.Parameter(userIds).Contains(u.Id) && u.TenantId == currentUser.TenantId, ct) == userIds.Count;

    public static void AddMissing(UserGroup group, IEnumerable<Guid> userIds) =>
        group.Members.AddRange(userIds.Where(id => group.Members.All(m => m.UserId != id)).Select(id => new UserGroupMember { GroupId = group.Id, UserId = id }));

    public static Task<UserGroup?> LoadAsync(CoworkeeDbContext db, Guid id, CancellationToken ct) =>
        db.Set<UserGroup>().Include(g => g.Members).SingleOrDefaultAsync(g => g.Id == id, ct);
}

internal sealed class AddGroupMembersHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<AddGroupMembers, Result>
{
    public async Task<Result> HandleAsync(AddGroupMembers command, CancellationToken cancellationToken)
    {
        var group = await GroupMembers.LoadAsync(db, command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var wanted = command.UserIds.Distinct().ToList();
        if (!await GroupMembers.AllInTenantAsync(db, currentUser, wanted, cancellationToken))
        {
            return GroupMembers.UnknownUser;
        }

        GroupMembers.AddMissing(group, wanted);
        return Result.Success();
    }
}

internal sealed class RemoveGroupMembersHandler(CoworkeeDbContext db) : IHandler<RemoveGroupMembers, Result>
{
    public async Task<Result> HandleAsync(RemoveGroupMembers command, CancellationToken cancellationToken)
    {
        var group = await GroupMembers.LoadAsync(db, command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        group.Members.RemoveAll(m => command.UserIds.Contains(m.UserId));
        return Result.Success();
    }
}

internal sealed class MoveGroupMembersHandler(CoworkeeDbContext db) : IHandler<MoveGroupMembers, Result>
{
    public async Task<Result> HandleAsync(MoveGroupMembers command, CancellationToken cancellationToken)
    {
        var from = await GroupMembers.LoadAsync(db, command.FromId, cancellationToken);
        var to = command.ToId == command.FromId ? from : await GroupMembers.LoadAsync(db, command.ToId, cancellationToken);
        if (from is null || to is null)
        {
            return GroupErrors.NotFound;
        }

        if (from != to)
        {
            var moved = from.Members.Select(m => m.UserId).ToList();
            from.Members.Clear();
            GroupMembers.AddMissing(to, moved);
        }

        return Result.Success();
    }
}

internal sealed class SetGroupOwnersHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<SetGroupOwners, Result>
{
    public async Task<Result> HandleAsync(SetGroupOwners command, CancellationToken cancellationToken)
    {
        var group = await GroupMembers.LoadAsync(db, command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var wanted = command.UserIds.Distinct().ToList();
        if (!await GroupMembers.AllInTenantAsync(db, currentUser, wanted, cancellationToken))
        {
            return GroupMembers.UnknownUser;
        }

        GroupMembers.AddMissing(group, wanted);
        foreach (var member in group.Members)
        {
            member.IsOwner = wanted.Contains(member.UserId);
        }

        return Result.Success();
    }
}
