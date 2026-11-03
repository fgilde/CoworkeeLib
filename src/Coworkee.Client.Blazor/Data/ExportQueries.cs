using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

public static class ExportQueries
{
    /// <summary>The query string of an export: the list's $filter, $search and $orderby, the format and the columns.</summary>
    public static string Of(ExportFileFormat format, ODataQuery query, IReadOnlyCollection<string> columns) =>
        Of(format, query, columns.Select(c => (c, (string?)null)).ToList());

    /// <summary>
    /// The query string of an export: the list's $filter, $search and $orderby, the format, the columns with their headers (null: the field name)
    /// and the file name without extension (null: the set's).
    /// </summary>
    public static string Of(ExportFileFormat format, ODataQuery query, IReadOnlyCollection<(string Field, string? Header)> columns, string? fileName = null)
    {
        var url = (query with { Count = false, Top = null, Skip = null, Expand = null, Select = null }).ToQueryString();
        return url + (url.Contains('?') ? "&" : "?") + $"format={format.ToString().ToLowerInvariant()}"
               + (columns.Count == 0 ? string.Empty : $"&columns={Uri.EscapeDataString(ExportColumns.Format(columns))}")
               + (string.IsNullOrWhiteSpace(fileName) ? string.Empty : $"&fileName={Uri.EscapeDataString(fileName)}");
    }
}
