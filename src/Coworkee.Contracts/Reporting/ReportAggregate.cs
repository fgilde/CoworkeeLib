namespace Coworkee.Contracts.Reporting;

/// <summary>One value of a report query.</summary>
/// <param name="EmptyAsZero">Average only: rows without a value count as 0, so the average is the sum divided by all rows (default: the average of the rows that have a value).</param>
/// <param name="Format">Grouped tables: the value's own number format; null takes the widget's (counts: whole numbers). Currency uses the currency of the value's own data set.</param>
/// <param name="Unit">Grouped tables: shown after the number, e.g. "kg"; null shows none.</param>
public sealed record ReportAggregate(
    AggregateFunction Function = AggregateFunction.Count, string? Field = null, string? Label = null, bool EmptyAsZero = false, NumberFormat? Format = null, string? Unit = null);
