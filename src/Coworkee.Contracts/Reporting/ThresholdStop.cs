namespace Coworkee.Contracts.Reporting;

/// <summary>From <see cref="Value"/> on (up to the next stop) a value takes <see cref="Color"/>: a theme colour (success, warning, error, info) or #rrggbb.</summary>
/// <param name="Label">Says what the band means ("On target"); shown next to the colour, so the colour is never the only sign.</param>
public sealed record ThresholdStop(double Value, string Color, string? Label = null);
