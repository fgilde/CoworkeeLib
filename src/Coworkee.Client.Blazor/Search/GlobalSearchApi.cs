using Coworkee.Contracts.Search;

namespace Coworkee.Client.Blazor.Search;

internal sealed class GlobalSearchApi(HttpClient http) : Api.ApiClientBase(http), IGlobalSearchApi
{
    private const string Root = "api/v1/search";

    public IAsyncEnumerable<GlobalSearchEvent> StreamAsync(string text, IReadOnlyCollection<string>? types = null, int? limit = null, CancellationToken cancellationToken = default) =>
        StreamAsync<GlobalSearchEvent>($"{Root}/stream?q={Uri.EscapeDataString(text)}"
            + (types is { Count: > 0 } ? $"&types={Uri.EscapeDataString(string.Join(',', types))}" : string.Empty)
            + (limit is { } top ? $"&limit={top}" : string.Empty), cancellationToken);

    public Task<IReadOnlyList<GlobalSearchProviderDto>> GetProvidersAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<GlobalSearchProviderDto>>($"{Root}/providers", cancellationToken);

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetSearchFieldsAsync(CancellationToken cancellationToken = default) =>
        new Dictionary<string, IReadOnlyList<string>>(await GetAsync<Dictionary<string, IReadOnlyList<string>>>($"{Root}/fields", cancellationToken), StringComparer.OrdinalIgnoreCase);
}
