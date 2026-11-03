using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Roles;

internal sealed class DeleteRoleHandler(CoworkeeDbContext db, ICurrentUser currentUser, Users.AccountGuard guard, Users.AdminReach reach) : IHandler<DeleteRole, Result>
{
    public async Task<Result> HandleAsync(DeleteRole command, CancellationToken cancellationToken)
    {
        var role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == command.Id && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken);
        if (role is null)
        {
            return RoleNames.NotFound;
        }

        if (role.IsSystem)
        {
            return Error.Conflict("identity.role_system", "System roles cannot be deleted.");
        }

        // its members lose what it grants, wherever they work
        if ((await reach.CheckAllAsync(cancellationToken) ?? await guard.CheckRolesAsync([role.Id], cancellationToken)) is { } restricted)
        {
            return restricted;
        }

        db.RemoveRange(await db.Set<PermissionGrant>().Where(g => g.ProviderType == PermissionProviderType.Role && g.ProviderKey == role.Id).ToListAsync(cancellationToken));
        db.Remove(role);
        return Result.Success();
    }
}
