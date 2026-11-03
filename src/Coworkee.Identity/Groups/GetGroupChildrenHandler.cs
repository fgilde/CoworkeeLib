using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

internal sealed class GetGroupChildrenHandler(CoworkeeDbContext db, IPermissionChecker permissions, Users.AdminReach reach) : IHandler<GetGroupChildren, Result<IReadOnlyList<GroupNodeDto>>>
{
    public async Task<Result<IReadOnlyList<GroupNodeDto>>> HandleAsync(GetGroupChildren query, CancellationToken cancellationToken)
    {
        if (!await GroupNodes.CanBrowseAsync(permissions, cancellationToken))
        {
            return GroupNodes.NoBrowse;
        }

        var groups = db.Set<UserGroup>().AsQueryable();
        if (await reach.GroupIdsAsync(cancellationToken) is { } reached)
        {
            // a group-scoped administrator's roots are the top groups of his reach
            var ids = reached.ToList();
            groups = query.ParentId is null
                ? groups.Where(g => EF.Parameter(ids).Contains(g.Id) && (g.ParentId == null || !EF.Parameter(ids).Contains(g.ParentId.Value)))
                : groups.Where(g => EF.Parameter(ids).Contains(g.Id) && g.ParentId == query.ParentId);
        }
        else
        {
            groups = groups.Where(g => g.ParentId == query.ParentId);
        }

        return await groups.OrderBy(g => g.Name).ToNodes(db).ToListAsync(cancellationToken);
    }
}
