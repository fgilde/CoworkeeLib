namespace Coworkee.Contracts.Search;

/// <param name="Count">The hits of this page.</param>
public sealed record GlobalSearchProviderStatus(string Name, string Group, GlobalSearchProviderState State, int Count)
{
    /// <summary>How many records match in all, when the search asked for totals; null otherwise or when the provider cannot count.</summary>
    public long? Total { get; init; }
}
