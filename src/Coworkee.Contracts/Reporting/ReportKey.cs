namespace Coworkee.Contracts.Reporting;

/// <param name="Value">Invariant text of the value; the first day for date buckets, null for empty values.</param>
/// <param name="Filter">OData filter that selects the rows of this key, e.g. for cross filters and drill-down.</param>
public sealed record ReportKey(string? Value, string Label, string Filter);
