using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

internal sealed class SetGroupRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser, Roles.RoleOffer offer, Users.AccountGuard guard, Users.AdminReach reach)
    : IHandler<SetGroupRoles, Result>
{
    public async Task<Result> HandleAsync(SetGroupRoles command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().Include(g => g.Roles).SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null || !await reach.ReachesGroupAsync(group.Id, cancellationToken))
        {
            return GroupErrors.NotFound;
        }

        var wanted = command.RoleIds.Distinct().ToList();
        var valid = await db.Set<Role>().CountAsync(r => wanted.Contains(r.Id) && !r.IsSystem && r.TenantId == currentUser.TenantId, cancellationToken);
        if (valid != wanted.Count)
        {
            return Error.Validation(nameof(command.RoleIds), "Unknown role or system role. System roles cannot be assigned to groups.");
        }

        // the members gain or lose what the roles grant
        var added = wanted.Where(id => group.Roles.All(r => r.RoleId != id)).ToList();
        var removed = group.Roles.Where(r => !wanted.Contains(r.RoleId)).Select(r => r.RoleId).ToList();
        if (await guard.CheckRolesAsync(added.Concat(removed), cancellationToken) is { } restricted)
        {
            return restricted;
        }

        if (added.Count + removed.Count > 0 && await reach.CheckMembersAsync([group.Id], cancellationToken) is { } shared)
        {
            return shared;
        }

        // new users join the default group and get its roles
        if (group.IsDefault && added.Count > 0 && await offer.RefusedAsync(added, null, cancellationToken) is { } refused)
        {
            return refused;
        }

        group.Roles.RemoveAll(r => !wanted.Contains(r.RoleId));
        group.Roles.AddRange(wanted.Where(id => group.Roles.All(r => r.RoleId != id)).Select(id => new UserGroupRole { GroupId = group.Id, RoleId = id }));
        return Result.Success();
    }
}
