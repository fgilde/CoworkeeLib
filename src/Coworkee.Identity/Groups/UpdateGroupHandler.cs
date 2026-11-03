using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

internal sealed class UpdateGroupHandler(CoworkeeDbContext db, Users.AdminReach reach) : IHandler<UpdateGroup, Result>
{
    public async Task<Result> HandleAsync(UpdateGroup command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null || !await reach.ReachesGroupAsync(group.Id, cancellationToken))
        {
            return GroupErrors.NotFound;
        }

        // a null parent means "not specified": clients that predate the tree keep working
        if (command.Group.ParentId is { } parentId && parentId != group.ParentId)
        {
            return GroupErrors.UseMoveForParent;
        }

        var name = command.Group.Name.Trim();
        if (await db.Set<UserGroup>().AnyAsync(g => g.ParentId == group.ParentId && g.Name == name && g.Id != group.Id, cancellationToken))
        {
            return GroupErrors.NameTaken;
        }

        group.Name = name;
        group.Description = command.Group.Description;
        return Result.Success();
    }
}
