using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record MoveGroup(Guid Id, Guid? ParentId) : ICommand<Result>;

internal sealed class MoveGroupValidator : AbstractValidator<MoveGroup>
{
    public MoveGroupValidator() => RuleFor(c => c.Id).NotEmpty();
}

internal sealed class MoveGroupHandler(CoworkeeDbContext db) : IHandler<MoveGroup, Result>
{
    public async Task<Result> HandleAsync(MoveGroup command, CancellationToken cancellationToken)
    {
        var groups = db.Set<UserGroup>();
        var group = await groups.SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group?.Code is not { } oldCode)
        {
            return GroupErrors.NotFound;
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
        var levels = subtree.Select(g => GroupCodes.Depth(g.Code!)).DefaultIfEmpty(0).Max() - GroupCodes.Depth(oldCode);
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
