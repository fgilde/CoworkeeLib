using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Identity.Groups;

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
