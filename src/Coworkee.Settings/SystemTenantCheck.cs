using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Settings;

internal static class SystemTenantCheck
{
    public static async Task<bool> IsInSystemTenantAsync(this ICurrentUser user, IServiceProvider services, CancellationToken cancellationToken) =>
        user.TenantId is { } tenantId
        && services.GetService<ITenantDirectory>() is { } tenants
        && await tenants.IsSystemTenantAsync(tenantId, cancellationToken);
}
