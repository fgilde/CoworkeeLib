using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.ResourcePermissions;

internal sealed class RevokeResourcePermissionHandler(CoworkeeDbContext db, Users.AccountGuard guard, Users.AdminReach reach) : IHandler<RevokeResourcePermission, Result>
{
    public async Task<Result> HandleAsync(RevokeResourcePermission command, CancellationToken cancellationToken)
    {
        var permission = await db.Set<ResourcePermission>().SingleOrDefaultAsync(
            p => p.Id == command.Id && p.ResourceType == command.ResourceType && p.ResourceId == command.ResourceId, cancellationToken);
        if (permission is null || !await ResourcePrincipals.ReachesAsync(reach, permission.PrincipalType, permission.PrincipalId, cancellationToken))
        {
            return Error.NotFound("identity.resource_permission_not_found", "Resource permission not found.");
        }

        if (await guard.CheckResourceGrantAsync(permission.RoleId, command.ResourceType, command.ResourceId, cancellationToken) is { } restricted)
        {
            return restricted;
        }

        db.Remove(permission);
        return Result.Success();
    }
}
