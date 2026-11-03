using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;

namespace Coworkee.Identity.Groups;

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
