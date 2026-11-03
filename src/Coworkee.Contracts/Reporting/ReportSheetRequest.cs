namespace Coworkee.Contracts.Reporting;

/// <summary>A table as a widget shows it, written to an Excel workbook; cells are text, numbers or null.</summary>
public sealed record ReportSheetRequest(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<System.Text.Json.JsonElement>> Rows);
