using Coworkee.Core.Results;
using Coworkee.Core.Security;

namespace Coworkee.AuthServer.Clients;

/// <summary>Clients and scopes serve the whole installation, so only the system organisation's administrators manage them.</summary>
internal sealed class HostAccess(ICurrentUser currentUser, ITenantDirectory tenants)
{
    public static readonly Error Forbidden = Error.Forbidden("auth.host_only", "Clients and scopes are managed from the system organisation.");

    public async Task<bool> IsHostAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId && await tenants.IsSystemTenantAsync(tenantId, cancellationToken);
}
