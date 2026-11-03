using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

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
