using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

internal sealed class CreateGroupHandler(CoworkeeDbContext db, Users.AdminReach reach) : IHandler<CreateGroup, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateGroup command, CancellationToken cancellationToken) =>
        await reach.CheckParentAsync(command.Group.ParentId, cancellationToken) is { } refused ? refused : await AddAsync(db, command.Group, cancellationToken);

    /// <summary>Adds a group under its parent with the next free code; shared with the group import.</summary>
    internal static async Task<Result<Guid>> AddAsync(CoworkeeDbContext db, GroupRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var groups = db.Set<UserGroup>();
        var parentId = request.ParentId;
        string? parentCode = null;
        if (parentId is not null)
        {
            // the tenant filter hides parents of other tenants; a parent without a code has not been through the migrations host yet
            parentCode = await groups.Where(g => g.Id == parentId).Select(g => g.Code).SingleOrDefaultAsync(cancellationToken);
            if (parentCode is null)
            {
                return GroupErrors.ParentNotFound;
            }

            if (GroupCodes.Depth(parentCode) >= GroupCodes.MaxDepth)
            {
                return GroupErrors.TooDeep;
            }
        }

        if (await groups.AnyAsync(g => g.ParentId == parentId && g.Name == name, cancellationToken))
        {
            return GroupErrors.NameTaken;
        }

        var group = new UserGroup
        {
            Name = name,
            Description = request.Description,
            ParentId = parentId,
            Code = await GroupCodes.NextAsync(groups, parentCode, parentId, cancellationToken),
        };
        db.Add(group);
        return group.Id;
    }
}
