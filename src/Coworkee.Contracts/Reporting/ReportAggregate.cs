namespace Coworkee.Contracts.Reporting;

/// <summary>One value of a report query.</summary>
/// <param name="EmptyAsZero">Average only: rows without a value count as 0, so the average is the sum divided by all rows (default: the average of the rows that have a value).</param>
public sealed record ReportAggregate(AggregateFunction Function = AggregateFunction.Count, string? Field = null, string? Label = null, bool EmptyAsZero = false);
