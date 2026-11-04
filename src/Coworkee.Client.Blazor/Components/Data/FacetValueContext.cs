namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// A value to render: the raw value as text (an option's value, a cell's value; <c>null</c> for chosen facets, which keep only their label)
/// and the label it reads as.
/// </summary>
public sealed record FacetValueContext(string? Value, string Label)
{
    /// <summary>The extras of a facet option, e.g. <c>color</c>.</summary>
    public IReadOnlyDictionary<string, string>? Meta { get; init; }
}
