using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Contracts.DataFilters;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData.DataFilters;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

internal sealed class DeleteGroupHandler(CoworkeeDbContext db, Users.AccountGuard guard, Users.AdminReach reach) : IHandler<DeleteGroup, Result>
{
    public async Task<Result> HandleAsync(DeleteGroup command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null || !await reach.ReachesGroupAsync(group.Id, cancellationToken))
        {
            return GroupErrors.NotFound;
        }

        // members, roles and selections go with their group through the foreign keys; the self reference restricts, so the whole subtree is removed in one unit
        var subtree = group.Code is { } code
            ? await db.Set<UserGroup>().Where(g => g.Code!.StartsWith(code + ".")).ToListAsync(cancellationToken)
            : [];
        subtree.Add(group);
        var ids = subtree.Select(g => g.Id).ToList();

        // the members lose what the groups grant
        // the default group, also below a group in reach, belongs to the whole organisation; the caller's top groups to an administrator above him
        if ((await guard.CheckGroupsAsync(ids, cancellationToken) ?? await reach.CheckMembersAsync(ids, cancellationToken)
                ?? (subtree.Any(g => g.IsDefault) ? await reach.CheckAllAsync(cancellationToken) : null)
                ?? await reach.CheckNotRootAsync(group.Id, cancellationToken)) is { } restricted)
        {
            return restricted;
        }

        db.RemoveRange(await db.Set<PermissionGrant>().Where(g => g.ProviderType == PermissionProviderType.Group && EF.Parameter(ids).Contains(g.ProviderKey)).ToListAsync(cancellationToken));
        db.RemoveRange(await db.Set<ResourcePermission>().Where(p => p.PrincipalType == PrincipalType.Group && EF.Parameter(ids).Contains(p.PrincipalId)).ToListAsync(cancellationToken));
        // their data filters go along (through the change tracker: the realtime signal fires)
        db.RemoveRange(await db.Set<DataFilter>().Where(f => f.Layer == DataFilterLayer.Group && EF.Parameter(ids).Contains(f.LayerKey!.Value)).ToListAsync(cancellationToken));
        db.RemoveRange(subtree);
        return Result.Success();
    }
}
