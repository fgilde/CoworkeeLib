using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Search;

/// <summary>A part of a hit's title that matched the search, to highlight.</summary>
public sealed record HighlightRange(int Start, int Length);

/// <summary>One record found by the global search.</summary>
/// <param name="Provider">The name of the provider, also the word of a type filter (<c>users:max</c>).</param>
/// <param name="Group">The heading the hit shows under.</param>
public sealed record GlobalSearchHit(string Provider, string Group, string Title)
{
    public string? Subtitle { get; init; }

    /// <summary>Where the hit leads in the app, e.g. <c>/admin/users/{id}</c>.</summary>
    public string? Url { get; init; }

    public string? EntitySet { get; init; }

    public string? Id { get; init; }

    /// <summary>0 to 1, comparable across providers: 1 is an exact match.</summary>
    public double Score { get; init; }

    public IReadOnlyList<HighlightRange> Highlights { get; init; } = [];

    /// <summary>An image URL (or a small data URL).</summary>
    public string? Thumbnail { get; init; }

    /// <summary>A Material icon name (<c>Person</c>) or SVG markup.</summary>
    public string? Icon { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<GlobalSearchProviderState>))]
public enum GlobalSearchProviderState
{
    Done,
    TimedOut,
    Failed,
}

/// <summary>One line of the streamed search (application/x-ndjson): a hit as soon as it is found, or a provider that finished.</summary>
public sealed record GlobalSearchEvent(string Provider, GlobalSearchHit? Hit = null, GlobalSearchProviderState? State = null);

public sealed record GlobalSearchProviderDto(string Name, string Group, string? Icon);

public sealed record GlobalSearchProviderStatus(string Name, string Group, GlobalSearchProviderState State, int Count);

/// <summary>All hits, best first, and how each provider ended.</summary>
public sealed record GlobalSearchResult(IReadOnlyList<GlobalSearchHit> Hits, IReadOnlyList<GlobalSearchProviderStatus> Providers);
