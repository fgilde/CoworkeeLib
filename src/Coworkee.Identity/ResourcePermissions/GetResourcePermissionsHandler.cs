using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.ResourcePermissions;

internal sealed class GetResourcePermissionsHandler(CoworkeeDbContext db, Users.AdminReach reach) : IHandler<GetResourcePermissions, Result<IReadOnlyList<ResourcePermissionDto>>>
{
    public async Task<Result<IReadOnlyList<ResourcePermissionDto>>> HandleAsync(GetResourcePermissions query, CancellationToken cancellationToken)
    {
        var grants = await db.Set<ResourcePermission>()
            .Where(p => p.ResourceType == query.ResourceType && p.ResourceId == query.ResourceId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new ResourcePermissionDto(p.Id, p.PrincipalType, p.PrincipalId, p.RoleId))
            .ToListAsync(cancellationToken);
        if (await reach.AllAsync(cancellationToken))
        {
            return grants;
        }

        // grants to users and groups out of the caller's reach stay unknown to him
        var shown = new List<ResourcePermissionDto>();
        foreach (var grant in grants)
        {
            if (await ResourcePrincipals.ReachesAsync(reach, grant.PrincipalType, grant.PrincipalId, cancellationToken))
            {
                shown.Add(grant);
            }
        }

        return shown;
    }
}
