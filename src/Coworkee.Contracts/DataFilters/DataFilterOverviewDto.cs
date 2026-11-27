namespace Coworkee.Contracts.DataFilters;

/// <param name="LayerFilters">Filters of the overview's layer on the set.</param>
/// <param name="Linked">The set has a link (reported in the system tenant only).</param>
public sealed record DataFilterSetStateDto(string EntitySet, int LayerFilters, bool Linked);

/// <param name="Layer">System in the system tenant, otherwise Tenant.</param>
/// <param name="TenantName">The administrator's tenant.</param>
public sealed record DataFilterOverviewDto(DataFilterLayer Layer, string? TenantName, IReadOnlyList<DataFilterSetStateDto> Sets);
