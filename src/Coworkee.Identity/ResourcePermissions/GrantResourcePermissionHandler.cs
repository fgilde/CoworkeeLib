using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.ResourcePermissions;

internal sealed class GrantResourcePermissionHandler(CoworkeeDbContext db, ICurrentUser currentUser, Users.AccountGuard guard, Users.AdminReach reach)
    : IHandler<GrantResourcePermission, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(GrantResourcePermission command, CancellationToken cancellationToken)
    {
        var grant = command.Grant;
        var principalExists = (grant.PrincipalType == PrincipalType.User
            ? await db.Set<User>().AnyAsync(u => u.Id == grant.PrincipalId && u.TenantId == currentUser.TenantId, cancellationToken)
            : await db.Set<UserGroup>().AnyAsync(g => g.Id == grant.PrincipalId, cancellationToken))
            && await ResourcePrincipals.ReachesAsync(reach, grant.PrincipalType, grant.PrincipalId, cancellationToken);
        var roleExists = await db.Set<Role>().AnyAsync(r => r.Id == grant.RoleId && !r.IsSystem && r.TenantId == currentUser.TenantId, cancellationToken);
        if (!principalExists || !roleExists)
        {
            return Error.Validation(nameof(command.Grant), "Unknown principal or role.");
        }

        if (await db.Set<ResourcePermission>().AnyAsync(p => p.ResourceType == command.ResourceType && p.ResourceId == command.ResourceId
                && p.PrincipalType == grant.PrincipalType && p.PrincipalId == grant.PrincipalId && p.RoleId == grant.RoleId, cancellationToken))
        {
            return Error.Conflict("identity.resource_permission_exists", "This permission is already granted.");
        }

        if (await guard.CheckResourceGrantAsync(grant.RoleId, command.ResourceType, command.ResourceId, cancellationToken) is { } restricted)
        {
            return restricted;
        }

        var permission = new ResourcePermission
        {
            ResourceType = command.ResourceType,
            ResourceId = command.ResourceId,
            PrincipalType = grant.PrincipalType,
            PrincipalId = grant.PrincipalId,
            RoleId = grant.RoleId,
        };
        db.Add(permission);
        return permission.Id;
    }
}
