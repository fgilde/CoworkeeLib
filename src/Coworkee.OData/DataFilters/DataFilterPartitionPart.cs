using Coworkee.Application.Authorization;
using Coworkee.Application.Groups;
using Coworkee.Contracts.DataFilters;
using Coworkee.Core.Security;
using Coworkee.OData.ClientEntities;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.DataFilters;

/// <summary>
/// The data filters of the user's organisation and the System ones as a fingerprint of their ids and versions, links included, plus what decides which of
/// them apply: the user's active roles that carry a role filter and, when there are group filters, his active groups. A copy of rows starts over when any of
/// them changes. Only ever hashed into the partition, the client never sees it; null when there are no filters.
/// </summary>
// ponytail: one fingerprint for all sets, so any filter change resyncs every copy; per-set partitions if filters change often
internal sealed class DataFilterPartitionPart(DataFilterCache cache, ICurrentUser user, IPermissionChecker permissions, IServiceProvider services) : IClientEntityPartitionPart
{
    public async Task<string?> GetAsync(CancellationToken ct)
    {
        var rows = await cache.FiltersAsync(services, user.TenantId, ct);
        if (rows.Filters.Count == 0 && rows.Links.Count == 0)
        {
            return null;
        }

        var parts = rows.Filters.OrderBy(f => f.Id).Select(f => $"{f.Id}:{f.Version}")
            .Concat(rows.Links.OrderBy(l => l.EntitySet, StringComparer.Ordinal).Select(l => $"{l.EntitySet}.{l.Property}>{l.TargetSet}"))
            .ToList();
        // who bypasses the filters and whether System filters apply to him decide what he reads: both are part of the fingerprint
        parts.Add($"bypass:{await permissions.IsGrantedAsync(DataFilterPermissions.Bypass, ct)}");
        parts.Add($"system:{user.TenantId is { } tenantId && services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenantId, ct)}");
        var roleFilters = rows.Filters.Where(f => f.Layer == DataFilterLayer.Role).Select(f => f.LayerKey).ToHashSet();
        if (roleFilters.Count > 0)
        {
            parts.AddRange((await permissions.GetRoleIdsAsync(ct)).Where(r => roleFilters.Contains(r)).Order().Select(r => $"role:{r}"));
        }

        if (rows.Filters.Any(f => f.Layer == DataFilterLayer.Group) && services.GetService<IGroupScope>() is { } groups)
        {
            parts.AddRange((await groups.ActiveAsync(ct)).Distinct().Order().Select(g => $"group:{g}"));
        }

        return string.Join(",", parts);
    }
}
