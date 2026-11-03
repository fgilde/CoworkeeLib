namespace Coworkee.Contracts.Data;

/// <param name="Query">The query string of the export: $filter, $search, $orderby, columns and format.</param>
/// <param name="Recipients">Addresses that get the file besides the owner.</param>
public sealed record SaveExportScheduleRequest(
    string EntitySet, string Name, string? Query, Reporting.ReportSchedule Schedule, string? FileName = null, IReadOnlyList<string>? Recipients = null);
