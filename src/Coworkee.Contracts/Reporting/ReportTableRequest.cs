namespace Coworkee.Contracts.Reporting;

/// <summary>
/// A table grouped by several fields: the groups of the main set with its values, next to them the values of joined sets grouped by the same fields
/// (texts match ignoring case, empty equals null), sorted and paged on the server.
/// </summary>
/// <param name="GroupFields">1 to 8 fields; every joined set needs each of them.</param>
/// <param name="OrderBy">Columns to sort by; ties are broken by every group field ascending.</param>
public sealed record ReportTableRequest(
    string EntitySet,
    string? Filter,
    IReadOnlyList<string> GroupFields,
    IReadOnlyList<ReportAggregate> Aggregates,
    IReadOnlyList<ReportTableJoin>? Joins = null,
    IReadOnlyList<ReportTableSort>? OrderBy = null,
    int Skip = 0,
    int Top = 50);

/// <summary>Values of another set for the groups of the main set; groups only the joined set has are left out, missing values are null.</summary>
public sealed record ReportTableJoin(string EntitySet, string? Filter, IReadOnlyList<ReportAggregate> Aggregates);

/// <param name="Column">0..GroupFields-1: keys; then the values: main aggregates, then each join's in order.</param>
public sealed record ReportTableSort(int Column, bool Descending = false);
