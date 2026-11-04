using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Permissions;

internal sealed class DeleteCustomPermissionHandler(
    CoworkeeDbContext db, ICurrentUser currentUser, ITenantDirectory tenants, IPermissionDefinitionManager definitions, CustomPermissionDefinitions stored,
    Users.AccountGuard guard, Users.AdminReach reach)
    : IHandler<DeleteCustomPermission, Result>
{
    public async Task<Result> HandleAsync(DeleteCustomPermission command, CancellationToken cancellationToken)
    {
        var found = await CustomPermissionRules.FindAsync(db, currentUser, tenants, definitions, stored, command.Name, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        // revoking it from everyone: only an administrator of all groups who holds it himself
        if ((await reach.CheckAllAsync(cancellationToken) ?? await guard.CheckGrantAsync([command.Name], cancellationToken)) is { } restricted)
        {
            return restricted;
        }

        // the interceptors drop the cached permissions and definitions and tell the clients after the commit
        db.RemoveRange(await db.Set<PermissionGrant>().Where(g => g.Name == command.Name).ToListAsync(cancellationToken));
        db.Remove(found.Value);
        return Result.Success();
    }
}
