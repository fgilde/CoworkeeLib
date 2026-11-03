using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.ClientEntities;

internal sealed class ClientEntityChangeLog(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock, IOptions<ClientEntityOptions> options) : IClientEntityChangeLog
{
    public void Touch(Type entityType, IEnumerable<Guid> ids)
    {
        if (options.Value.Resolve(entityType) is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        db.AddRange(ids.Distinct().Select(id => new ClientEntityTouch { TenantId = currentUser.TenantId, EntityType = entityType.FullName!, EntityId = id, TouchedAt = now }));
    }

    /// <summary>The ids of <paramref name="entityType"/> touched after <paramref name="from"/>, at most <paramref name="take"/>.</summary>
    public Task<List<Guid>> SinceAsync(Type entityType, DateTimeOffset from, int take, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        return db.Set<ClientEntityTouch>().AsNoTracking()
            .Where(t => t.EntityType == entityType.FullName && t.TouchedAt > from && (t.TenantId == null || t.TenantId == tenantId))
            .Select(t => t.EntityId).Distinct().Take(take).ToListAsync(cancellationToken);
    }
}
