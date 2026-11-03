using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

// ponytail: two concurrent cross moves ("A under B", "B under A") both pass the cycle check and commit a ParentId cycle;
// serializable isolation or re-reading the parent's code right before the save closes it
internal sealed class MoveGroupHandler(CoworkeeDbContext db, Users.AdminReach reach) : IHandler<MoveGroup, Result>
{
    public async Task<Result> HandleAsync(MoveGroup command, CancellationToken cancellationToken)
    {
        var groups = db.Set<UserGroup>();
        var group = await groups.SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group?.Code is not { } oldCode || !await reach.ReachesGroupAsync(group.Id, cancellationToken))
        {
            return GroupErrors.NotFound;
        }

        // also when it stays where it is: a group-scoped administrator learns nothing about parents out of his reach
        if (await reach.CheckParentAsync(command.ParentId, cancellationToken) is { } refused)
        {
            return refused;
        }

        string? parentCode = null;
        if (command.ParentId is { } parentId)
        {
            parentCode = await groups.Where(g => g.Id == parentId).Select(g => g.Code).SingleOrDefaultAsync(cancellationToken);
            if (parentCode is null)
            {
                return GroupErrors.ParentNotFound;
            }

            if (parentId == group.Id || GroupCodes.IsDescendant(parentCode, oldCode))
            {
                return GroupErrors.MoveIntoOwnSubtree;
            }
        }

        if (command.ParentId == group.ParentId)
        {
            return Result.Success();
        }

        var subtree = await groups.Where(g => g.Code!.StartsWith(oldCode + ".")).ToListAsync(cancellationToken);
        var levels = subtree.Select(g => GroupCodes.Depth(g.Code!)).DefaultIfEmpty(GroupCodes.Depth(oldCode)).Max() - GroupCodes.Depth(oldCode);
        if ((parentCode is null ? 1 : GroupCodes.Depth(parentCode) + 1) + levels > GroupCodes.MaxDepth)
        {
            return GroupErrors.TooDeep;
        }

        if (await groups.AnyAsync(g => g.ParentId == command.ParentId && g.Name == group.Name, cancellationToken))
        {
            return GroupErrors.NameTaken;
        }

        var newCode = await GroupCodes.NextAsync(groups, parentCode, command.ParentId, cancellationToken);
        foreach (var descendant in subtree)
        {
            descendant.Code = newCode + descendant.Code![oldCode.Length..];
        }

        group.Code = newCode;
        group.ParentId = command.ParentId;
        return Result.Success();
    }
}
