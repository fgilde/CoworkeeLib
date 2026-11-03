namespace Coworkee.Contracts.Search;

/// <summary>A part of a hit's title that matched the search, to highlight.</summary>
public sealed record HighlightRange(int Start, int Length);
