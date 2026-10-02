using System.Text.Json.Nodes;

namespace Coworkee.Search;

public enum SearchFieldType
{
    /// <summary>Exact values: ids, keys, list values; filterable, sortable, facetable; also matched by full text.</summary>
    Keyword,

    /// <summary>Analysed text; a keyword sub field keeps exact filters and sorting working.</summary>
    Text,
    Date,
    Long,
    Double,
    Boolean,
}

public sealed record SearchField(string Name, SearchFieldType Type);

/// <summary>The fields of one index; fields not listed are stored but not searchable.</summary>
public sealed record SearchSchema(string Alias, IReadOnlyList<SearchField> Fields);

/// <summary>One document; <see cref="Id"/> is its key, <see cref="Body"/> the indexed fields (dotted names become nested objects).</summary>
public sealed record SearchDocument(string Id, JsonObject Body);

public enum FilterOperator
{
    HasValue,
    HasNoValue,
    Equals,
    NotEquals,
    Contains,
    DoesNotContain,
    FuzzyEquals,
    FuzzyContains,
    GreaterThanOrEquals,
    LessThanOrEquals,
    Between,
    IdentifierEquals,
    IdentifierNotEquals,
}

/// <summary>A condition on one field; several values of Equals/IdentifierEquals mean "any of".</summary>
public sealed record SearchFilter(string Field, FilterOperator Operator, IReadOnlyList<string>? Values = null);

public sealed record SearchSort(string Field, bool Descending = false);

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

public sealed record SearchHit(string Id, double? Score, JsonObject? Fields);

public sealed record FacetValue(string Value, long Count);

/// <summary>A document the index refused, with the reason given by the index.</summary>
public sealed record SearchWriteError(string Id, string Reason);

/// <summary>The index refused a request; <see cref="IsInvalidRequest"/> when the request itself was at fault (bad value, bad cursor), otherwise the index is unavailable.</summary>
public sealed class SearchIndexException(string message, bool isInvalidRequest) : Exception(message)
{
    public bool IsInvalidRequest { get; } = isInvalidRequest;
}

public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, long Total, IReadOnlyDictionary<string, IReadOnlyList<FacetValue>> Facets, string? Cursor);

/// <summary>Full text index; schemas are owned by the application, documents are plain JSON.</summary>
public interface ISearchIndex
{
    /// <summary>Creates the alias with a first index if missing and adds new fields to the mapping.</summary>
    Task EnsureAsync(SearchSchema schema, CancellationToken cancellationToken);

    /// <summary>Creates a fresh index for <paramref name="schema"/> without touching the alias; returns its name for writing and <see cref="SwapAsync"/>.</summary>
    Task<string> CreateIndexAsync(SearchSchema schema, CancellationToken cancellationToken);

    /// <summary>Points the alias at <paramref name="index"/> and removes the indexes it pointed at before.</summary>
    Task SwapAsync(string alias, string index, CancellationToken cancellationToken);

    /// <summary>Adds or replaces documents; <paramref name="target"/> is an alias or an index name. Returns the documents that were refused.</summary>
    Task<IReadOnlyList<SearchWriteError>> UpsertAsync(string target, IEnumerable<SearchDocument> documents, CancellationToken cancellationToken);

    Task DeleteAsync(string target, IEnumerable<string> ids, CancellationToken cancellationToken);

    /// <summary>Removes every document matching all filters.</summary>
    Task DeleteWhereAsync(string target, IReadOnlyList<SearchFilter> filters, CancellationToken cancellationToken);

    Task<SearchResult> SearchAsync(string alias, SearchQuery query, CancellationToken cancellationToken);

    /// <summary>Type-ahead: documents whose <paramref name="fields"/> start with or fuzzily match <paramref name="prefix"/>.</summary>
    Task<IReadOnlyList<SearchHit>> SuggestAsync(string alias, string prefix, IReadOnlyList<string> fields, IReadOnlyList<SearchFilter> filters, int size, CancellationToken cancellationToken);
}
