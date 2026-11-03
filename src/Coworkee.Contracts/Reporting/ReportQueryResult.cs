namespace Coworkee.Contracts.Reporting;

/// <summary>One value per aggregate of the request, in its order.</summary>
public sealed record ReportQueryResult(IReadOnlyList<ReportQueryRow> Rows, long ElapsedMilliseconds);
