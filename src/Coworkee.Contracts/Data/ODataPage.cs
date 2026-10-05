namespace Coworkee.Contracts.Data;

public sealed record ODataPage<T>(IReadOnlyList<T> Items, long? Count, IReadOnlyList<FacetGroupDto> Facets);
