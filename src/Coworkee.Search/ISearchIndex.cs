namespace Coworkee.Search;

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
