using Coworkee.Core.Security;
using Coworkee.Infrastructure.Auditing;
using Coworkee.OData;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing.OData;

/// <summary>The audit of the own tenant; the system tenant also sees changes outside any tenant.</summary>
internal sealed class AuditEntryODataFilter(ICurrentUser currentUser, IServiceProvider services) : IODataEntityFilter<AuditEntry>
{
    public async Task<IQueryable<AuditEntry>> ApplyAsync(IQueryable<AuditEntry> query, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var system = tenantId is { } tenant && services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenant, cancellationToken);
        return query.Where(e => e.TenantId == tenantId || (system && e.TenantId == null));
    }
}
