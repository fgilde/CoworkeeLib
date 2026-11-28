using Nextended.Core.Attributes;
using Coworkee.Application.Setup;
using Coworkee.Contracts.DataFilters;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData.DataFilters;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Setup;

/// <summary>Removes the Group and Role filters of a tenant whose group or role no longer exists (old versions left them behind; deleting now takes them along). Idempotent.</summary>
[RegisterAs(typeof(ISeedContributor))]
internal sealed class DataFilterOrphansSeed(CoworkeeDbContext db) : ISeedContributor
{
    public async Task SeedAsync(SeedContext context, CancellationToken cancellationToken)
    {
        var tenantId = context.TenantId;
        // through the change tracker: the realtime signal fires
        db.RemoveRange(await db.Set<DataFilter>().IgnoreQueryFilters().Where(f => f.TenantId == tenantId
            && ((f.Layer == DataFilterLayer.Group && !db.Set<UserGroup>().IgnoreQueryFilters().Any(g => g.Id == f.LayerKey && g.TenantId == tenantId))
                || (f.Layer == DataFilterLayer.Role && !db.Set<Role>().IgnoreQueryFilters().Any(r => r.Id == f.LayerKey && (r.TenantId == null || r.TenantId == tenantId))))).ToListAsync(cancellationToken));
    }
}
