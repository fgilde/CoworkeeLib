using Coworkee.Core.Security;
using Coworkee.OData.ClientEntities;

namespace Coworkee.OData.DataFilters;

/// <summary>
/// The data filters that apply to the user (own organisation and System) as a fingerprint of their ids and versions, links included: a copy of rows
/// starts over when any of them changes. Only ever hashed into the partition, the client never sees it; null when there are none.
/// </summary>
// ponytail: one fingerprint for all sets, so any filter change resyncs every copy; per-set partitions if filters change often
internal sealed class DataFilterPartitionPart(DataFilterCache cache, ICurrentUser user, IServiceProvider services) : IClientEntityPartitionPart
{
    public async Task<string?> GetAsync(CancellationToken ct)
    {
        var rows = await cache.FiltersAsync(services, user.TenantId, ct);
        return rows.Filters.Count == 0 && rows.Links.Count == 0
            ? null
            : string.Join(",", rows.Filters.OrderBy(f => f.Id).Select(f => $"{f.Id}:{f.Version}")
                .Concat(rows.Links.OrderBy(l => l.EntitySet, StringComparer.Ordinal).Select(l => $"{l.EntitySet}.{l.Property}>{l.TargetSet}")));
    }
}
