namespace Coworkee.Contracts.Search;

/// <summary>One line of the streamed search (application/x-ndjson): a hit as soon as it is found, or a provider that finished.</summary>
public sealed record GlobalSearchEvent(string Provider, GlobalSearchHit? Hit = null, GlobalSearchProviderState? State = null);
