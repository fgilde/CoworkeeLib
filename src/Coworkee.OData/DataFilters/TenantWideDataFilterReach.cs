using Coworkee.Application.Groups;
using Coworkee.Contracts.DataFilters;

namespace Coworkee.OData.DataFilters;

/// <summary>Without an identity module: every caller administers the filters of every group and role, and every key counts as existing.</summary>
internal sealed class TenantWideDataFilterReach : IDataFilterReach
{
    public Task<DataFilterReach> GetAsync(CancellationToken ct) => Task.FromResult(DataFilterReach.All);

    public Task<bool> ExistsAsync(DataFilterLayer layer, Guid key, CancellationToken ct) => Task.FromResult(true);
}
