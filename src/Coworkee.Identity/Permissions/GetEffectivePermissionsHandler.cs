using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Permissions;

internal sealed class GetEffectivePermissionsHandler(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker checker, Users.AdminReach reach)
    : IHandler<GetEffectivePermissions, Result<IReadOnlyList<string>>>
{
    public async Task<Result<IReadOnlyList<string>>> HandleAsync(GetEffectivePermissions query, CancellationToken cancellationToken)
    {
        if (!await db.Set<User>().AnyAsync(u => u.Id == query.UserId && u.TenantId == currentUser.TenantId, cancellationToken)
            || !await reach.ReachesUserAsync(query.UserId, cancellationToken))
        {
            return Error.NotFound("identity.user_not_found", "The user does not exist.");
        }

        IReadOnlyList<string> granted = (await ((PermissionChecker)checker).GetGrantedForAsync(query.UserId, currentUser.TenantId, cancellationToken))
            .Order(StringComparer.Ordinal)
            .ToList();
        return Result<IReadOnlyList<string>>.Success(granted);
    }
}
