using Coworkee.Contracts.Search;

namespace Coworkee.Client.Blazor.Search;

public interface IGlobalSearchApi
{
    /// <summary>Hits as the providers find them, then one line per provider that finished.</summary>
    IAsyncEnumerable<GlobalSearchEvent> StreamAsync(string text, IReadOnlyCollection<string>? types = null, int? limit = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GlobalSearchProviderDto>> GetProvidersAsync(CancellationToken cancellationToken = default);
}
