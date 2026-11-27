namespace Coworkee.Contracts.Reporting;

/// <param name="More">Whether rows follow after this page.</param>
public sealed record ReportTableResult(IReadOnlyList<ReportTableRow> Rows, bool More, long ElapsedMilliseconds);

/// <param name="Keys">One per group field, in the order of the request.</param>
/// <param name="Values">The main set's aggregates, then each join's in order.</param>
public sealed record ReportTableRow(IReadOnlyList<ReportKey?> Keys, IReadOnlyList<double?> Values);
