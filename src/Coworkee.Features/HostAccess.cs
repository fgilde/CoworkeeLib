using Coworkee.Core.Results;
using Coworkee.Core.Security;

namespace Coworkee.Features;

/// <summary>Tenants and editions belong to the system organisation; every admin holds the permissions, only the host's admins may use them.</summary>
internal sealed class HostAccess(ICurrentUser currentUser, ITenantDirectory tenants)
{
    public static readonly Error Forbidden = Error.Forbidden("features.host_only", "Tenants and editions are managed from the system organisation.");

    public async Task<bool> IsHostAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId && await tenants.IsSystemTenantAsync(tenantId, cancellationToken);
}
