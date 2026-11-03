using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Permissions;

internal sealed class GetGrantsHandler(CoworkeeDbContext db, ICurrentUser currentUser, Users.AdminReach reach) : IHandler<GetGrants, Result<IReadOnlyList<string>>>
{
    public async Task<Result<IReadOnlyList<string>>> HandleAsync(GetGrants query, CancellationToken cancellationToken) =>
        !await SetGrantsHandler.ReachesAsync(reach, query.ProviderType, query.ProviderKey, cancellationToken)
            ? SetGrantsHandler.NotFound
            : await db.Set<PermissionGrant>()
                .Where(g => g.ProviderType == query.ProviderType && g.ProviderKey == query.ProviderKey && g.TenantId == currentUser.TenantId)
                .OrderBy(g => g.Name)
                .Select(g => g.Name)
                .ToListAsync(cancellationToken);
}
