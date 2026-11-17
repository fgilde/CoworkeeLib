using Coworkee.Contracts.Search;

namespace Coworkee.Client.Blazor.Search;

public interface IGlobalSearchApi
{
    /// <summary>Hits as the providers find them, then one line per provider that finished.</summary>
    IAsyncEnumerable<GlobalSearchEvent> StreamAsync(string text, IReadOnlyCollection<string>? types = null, int? limit = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GlobalSearchProviderDto>> GetProvidersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of the search at once: each provider's hits after <paramref name="skip"/>, best first, and with <paramref name="totals"/> how many records
    /// match per provider (<see cref="GlobalSearchProviderStatus.Total"/>), e.g. a results page with a pager per type.
    /// </summary>
    Task<GlobalSearchResult> SearchAsync(string text, IReadOnlyCollection<string>? types = null, int? limit = null, int skip = 0, bool totals = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GlobalSearchResult([], []));

    /// <summary>The search fields each OData set declares (AddODataEntity SearchFields, AddGlobalSearch, ISearchable) that the user may read, as OData paths.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetSearchFieldsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(new Dictionary<string, IReadOnlyList<string>>());
}
