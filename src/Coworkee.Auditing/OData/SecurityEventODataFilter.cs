using Coworkee.Core.Security;
using Coworkee.Infrastructure.Auditing;
using Coworkee.OData;

namespace Coworkee.Auditing.OData;

internal sealed class SecurityEventODataFilter(ICurrentUser currentUser, IServiceProvider services) : IODataEntityFilter<SecurityEvent>
{
    public async Task<IQueryable<SecurityEvent>> ApplyAsync(IQueryable<SecurityEvent> query, CancellationToken cancellationToken)
    {
        var (tenantId, system) = await LogTenant.OfAsync(currentUser, services, cancellationToken);
        return query.Where(e => e.TenantId == tenantId || (system && e.TenantId == null));
    }
}
