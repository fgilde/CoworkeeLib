namespace Coworkee.Contracts.Search;

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
