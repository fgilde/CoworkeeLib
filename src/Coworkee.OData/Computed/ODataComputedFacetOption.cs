namespace Coworkee.OData.Computed;

/// <param name="Color">A CSS color for swatches and chips.</param>
public sealed record ODataComputedFacetOption(string Value, string Label, string OData, string? Color = null);
