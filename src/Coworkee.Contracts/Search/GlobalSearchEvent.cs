namespace Coworkee.Contracts.Search;

/// <summary>One line of the streamed search (application/x-ndjson): a hit as soon as it is found, or a provider that finished.</summary>
public sealed record GlobalSearchEvent(string Provider, GlobalSearchHit? Hit = null, GlobalSearchProviderState? State = null)
{
    /// <summary>On the provider's end, when totals were asked for: how many records match in all; null when the provider cannot count.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? Total { get; init; }
}
