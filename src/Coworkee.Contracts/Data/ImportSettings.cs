namespace Coworkee.Contracts.Data;

/// <param name="Columns">The field of each column by index, null to ignore the column.</param>
/// <param name="Culture">Reads numbers, dates and enum labels of text files, e.g. de-DE; empty: the user's language.</param>
public sealed record ImportSettings(
    IReadOnlyList<string?> Columns, ImportMode Mode = ImportMode.Create, string? KeyField = null, string? Culture = null, ImportParseOptions? Parse = null, bool DryRun = false);
