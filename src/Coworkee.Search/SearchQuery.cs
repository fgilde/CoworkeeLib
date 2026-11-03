namespace Coworkee.Search;

public sealed record SearchQuery
{
    /// <summary>Free text, matched against <see cref="TextFields"/>.</summary>
    public string? Term { get; init; }

    /// <summary>The term as a phrase instead of single words.</summary>
    public bool Exact { get; init; }

    /// <summary>All words must match (true) or any word is enough.</summary>
    public bool AllWords { get; init; } = true;

    public IReadOnlyList<string> TextFields { get; init; } = [];

    public IReadOnlyList<SearchFilter> Filters { get; init; } = [];

    /// <summary>Selected facet values: they filter the hits, while each facet still counts the values of its own field as if it were not selected (multi-select facets).</summary>
    public IReadOnlyList<SearchFilter> FacetFilters { get; init; } = [];

    public IReadOnlyList<string> Facets { get; init; } = [];

    public IReadOnlyList<SearchSort> Sort { get; init; } = [];

    public int Size { get; init; } = 50;

    /// <summary>Opaque position from a previous result; continues after its last hit.</summary>
    public string? Cursor { get; init; }

    /// <summary>Fields of the stored document to return; empty returns only ids.</summary>
    public IReadOnlyList<string> Include { get; init; } = [];
}
