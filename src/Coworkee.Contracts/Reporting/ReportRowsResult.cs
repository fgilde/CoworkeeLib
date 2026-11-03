namespace Coworkee.Contracts.Reporting;

/// <param name="Rows">One object per row, field path to value (enums by name, dates ISO).</param>
public sealed record ReportRowsResult(IReadOnlyList<System.Text.Json.JsonElement> Rows, long Count);
