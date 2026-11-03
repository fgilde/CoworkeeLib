using Microsoft.Extensions.Caching.Hybrid;

namespace Coworkee.Identity.Permissions;

// ponytail: any grant/role/group change invalidates every cached permission set; per-user tags when write volume matters
public sealed class PermissionCache(HybridCache cache)
{
    internal const string Tag = "coworkee:permissions";

    public Task InvalidateAsync(CancellationToken cancellationToken) => cache.RemoveByTagAsync(Tag, cancellationToken).AsTask();
}
