namespace Coworkee.Contracts.DataFilters;

/// <summary>A draft filter to try: the same fields as a save, nothing is stored.</summary>
public sealed record PreviewDataFilterRequest(string EntitySet, DataFilterLayer Layer, Guid? LayerKey, string Query);

/// <param name="Passing">Rows that pass the draft filter; null when counting timed out.</param>
/// <param name="Total">Rows the editor sees through the app's own row filters with no data filter at all; null when counting timed out.</param>
public sealed record DataFilterPreviewDto(long? Passing, long? Total, bool TimedOut);
