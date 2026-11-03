namespace Coworkee.Contracts.Data;

/// <summary>Overrides what the import detects in a file; null keeps the detected value.</summary>
public sealed record ImportParseOptions(string? Delimiter = null, string? Encoding = null, bool? HasHeader = null);
