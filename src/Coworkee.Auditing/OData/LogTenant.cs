using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing.OData;

/// <summary>The logs of the own tenant; the system tenant also sees rows outside any tenant (anonymous requests, sign-ins of unknown accounts).</summary>
internal static class LogTenant
{
    public static async Task<(Guid? TenantId, bool System)> OfAsync(ICurrentUser currentUser, IServiceProvider services, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var system = tenantId is { } tenant && services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenant, cancellationToken);
        return (tenantId, system);
    }
}
