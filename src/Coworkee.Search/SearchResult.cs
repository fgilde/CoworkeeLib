namespace Coworkee.Search;

public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, long Total, IReadOnlyDictionary<string, IReadOnlyList<FacetValue>> Facets, string? Cursor);
