namespace Coworkee.Contracts.Search;

/// <summary>All hits, best first, and how each provider ended.</summary>
public sealed record GlobalSearchResult(IReadOnlyList<GlobalSearchHit> Hits, IReadOnlyList<GlobalSearchProviderStatus> Providers);
