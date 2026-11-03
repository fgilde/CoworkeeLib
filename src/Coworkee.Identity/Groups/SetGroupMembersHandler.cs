using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;

namespace Coworkee.Identity.Groups;

internal sealed class SetGroupMembersHandler(CoworkeeDbContext db, ICurrentUser currentUser, Users.AccountGuard guard, Users.AdminReach reach) : IHandler<SetGroupMembers, Result>
{
    public async Task<Result> HandleAsync(SetGroupMembers command, CancellationToken cancellationToken)
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

        var joining = wanted.Where(id => group.Members.All(m => m.UserId != id));
        var leaving = group.Members.Where(m => !wanted.Contains(m.UserId)).Select(m => m.UserId);
        if (await GroupMembers.CheckAsync(guard, [group.Id], joining, leaving, cancellationToken) is { } refused)
        {
            return refused;
        }

        group.Members.RemoveAll(m => !wanted.Contains(m.UserId));
        GroupMembers.AddMissing(group, wanted);
        return Result.Success();
    }
}
