using Coworkee.Auditing.Requests;
using Coworkee.Core.Security;
using Coworkee.OData;

namespace Coworkee.Auditing.OData;

internal sealed class RequestLogODataFilter(ICurrentUser currentUser, IServiceProvider services) : IODataEntityFilter<RequestLogEntry>
{
    public async Task<IQueryable<RequestLogEntry>> ApplyAsync(IQueryable<RequestLogEntry> query, CancellationToken cancellationToken)
    {
        var (tenantId, system) = await LogTenant.OfAsync(currentUser, services, cancellationToken);
        return query.Where(e => e.TenantId == tenantId || (system && e.TenantId == null));
    }
}
