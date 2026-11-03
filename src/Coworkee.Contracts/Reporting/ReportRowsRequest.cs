namespace Coworkee.Contracts.Reporting;

/// <summary>A page of rows of an app source, with the chosen fields only.</summary>
public sealed record ReportRowsRequest(string EntitySet, IReadOnlyList<string> Columns, string? Filter = null, string? OrderBy = null, bool Descending = false, int Skip = 0, int Top = 50);
