namespace Coworkee.OData.Computed;

public sealed record ODataComputedFacet(string Label, IReadOnlyList<ODataComputedFacetOption> Options);
