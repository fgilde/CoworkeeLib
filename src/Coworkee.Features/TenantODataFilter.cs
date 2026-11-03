using Coworkee.Identity.Domain;
using Coworkee.OData;

namespace Coworkee.Features;

internal sealed class TenantODataFilter(HostAccess host) : IODataEntityFilter<Tenant>
{
    public async Task<IQueryable<Tenant>> ApplyAsync(IQueryable<Tenant> query, CancellationToken cancellationToken) =>
        await host.IsHostAsync(cancellationToken) ? query : query.Where(_ => false);
}
