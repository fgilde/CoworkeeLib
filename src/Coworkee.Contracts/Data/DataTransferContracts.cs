using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

public static class DataTransferPermissions
{
    public const string GroupName = "DataTransfer";

    /// <summary>Opens the import and export page; each set still needs its own permissions.</summary>
    public const string Use = "DataTransfer.Use";

    /// <summary>Recurring list exports by mail and exports mailed to other addresses; the set still needs its own permissions.</summary>
    public const string ExportSchedules = "DataTransfer.ExportSchedules";
}

/// <summary>
/// One line of the export stream (application/x-ndjson): <paramref name="Phase"/> is counting, reading, writing, ready, failed or cancelled.
/// The ready line carries the anonymous one-time <paramref name="Url"/> (relative to the API, five minutes) of the file; every line carries the <paramref name="JobId"/>.
/// </summary>
public sealed record DataExportProgress(string Phase, int Percent, string? Url = null, string? FileName = null, Guid? JobId = null, string? Message = null)
{
    /// <summary>The rows read so far (reading and writing lines).</summary>
    public int? Rows { get; init; }

    /// <summary>The rows the export holds, once they are counted.</summary>
    public int? Total { get; init; }

    public const string Counting = "counting";
    public const string Reading = "reading";
    public const string Writing = "writing";
    public const string Ready = "ready";
    public const string Failed = "failed";

    /// <summary>The user cancelled the export (<c>POST /api/v1/data/jobs/{id}/cancel</c>): no file, no mail.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// The last line of a stream with more rows than a request takes on servers before 0.0.1.50 (<c>JobId</c>: the export job). The stream now follows that
    /// job with reading, writing and ready lines and no longer sends it.
    /// </summary>
    public const string Queued = "queued";
}

/// <summary>
/// The <c>columns</c> parameter of an export: the fields in order, comma-separated, each with an optional header after a colon
/// (<c>Number:Shipment no.,Total</c>); a backslash escapes the next character, e.g. a comma in a header (<c>Weight:Weight\, kg</c>).
/// Without a header the column is headed by its field name. CSV and Excel show the headers; JSON keys stay the field names.
/// </summary>
public static class ExportColumns
{
    /// <summary>The value of <c>columns</c> for these fields and headers (null: the field name), escaped.</summary>
    public static string Format(IEnumerable<(string Field, string? Header)> columns) =>
        string.Join(',', columns.Select(c => string.IsNullOrEmpty(c.Header) ? Escape(c.Field) : $"{Escape(c.Field)}:{Escape(c.Header)}"));

    /// <summary>The fields and headers of a <c>columns</c> value; empty entries are skipped.</summary>
    public static IReadOnlyList<(string Field, string? Header)> Parse(string? value)
    {
        var columns = new List<(string Field, string? Header)>();
        var part = new System.Text.StringBuilder();
        string? field = null;
        value ??= string.Empty;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || value[i] == ',')
            {
                var text = part.ToString().Trim();
                if ((field ?? text).Length > 0)
                {
                    columns.Add(field is null ? (text, null) : (field, text.Length > 0 ? text : null));
                }

                (field, part.Length) = (null, 0);
            }
            else if (value[i] == '\\' && i + 1 < value.Length)
            {
                part.Append(value[++i]);
            }
            else if (value[i] == ':' && field is null)
            {
                (field, part.Length) = (part.ToString().Trim(), 0);
            }
            else
            {
                part.Append(value[i]);
            }
        }

        return columns;
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace(",", "\\,").Replace(":", "\\:");
}

/// <summary>An export written in the background and mailed to the requester and <paramref name="Recipients"/>; <paramref name="Query"/> is the export's query string.</summary>
public sealed record MailExportRequest(string? Query, IReadOnlyList<string>? Recipients = null, string? FileName = null);

/// <param name="Query">The query string of the export: $filter, $search, $orderby, columns and format.</param>
/// <param name="Recipients">Addresses that get the file besides the owner.</param>
public sealed record SaveExportScheduleRequest(
    string EntitySet, string Name, string? Query, Reporting.ReportSchedule Schedule, string? FileName = null, IReadOnlyList<string>? Recipients = null);

public sealed record ExportScheduleDto(
    Guid Id,
    string EntitySet,
    string Name,
    string? Query,
    Reporting.ReportSchedule Schedule,
    string? FileName,
    IReadOnlyList<string> Recipients,
    DateTimeOffset NextRunAt,
    DateTimeOffset? LastRunAt,
    string? LastError);

[JsonConverter(typeof(JsonStringEnumConverter<ImportMode>))]
public enum ImportMode
{
    Create,
    Update,
    Upsert,
}

[JsonConverter(typeof(JsonStringEnumConverter<DataTransferKind>))]
public enum DataTransferKind
{
    Import,
    Export,
}

[JsonConverter(typeof(JsonStringEnumConverter<DataTransferStatus>))]
public enum DataTransferStatus
{
    Draft,
    Queued,
    Running,
    Completed,
    Failed,
    Undone,

    /// <summary>An export the user stopped (<c>POST /api/v1/data/jobs/{id}/cancel</c>): it frees its slot, writes no file and sends no mail.</summary>
    Cancelled,
}

[JsonConverter(typeof(JsonStringEnumConverter<ExportFileFormat>))]
public enum ExportFileFormat
{
    Xlsx,
    Csv,
    Json,
}

/// <summary>A field rows can be mapped to; <see cref="KeyOnly"/> fields (the Id) only find rows to update.</summary>
public sealed record ImportFieldDto(string Name, string Label, string Type, bool Required, IReadOnlyList<string>? Choices = null, string? LookupSet = null, bool KeyOnly = false);

/// <summary>An entity set rows can be imported into. <see cref="KeyFields"/> find existing rows for update and upsert.</summary>
public sealed record ImportTargetDto(string EntitySet, IReadOnlyList<ImportFieldDto> Fields, bool CanUpdate, bool CanUndo, IReadOnlyList<string> KeyFields);

/// <summary>Overrides what the import detects in a file; null keeps the detected value.</summary>
public sealed record ImportParseOptions(string? Delimiter = null, string? Encoding = null, bool? HasHeader = null);

public sealed record ImportColumnDto(int Index, string Header, IReadOnlyList<string?> Samples, string? SuggestedField);

/// <summary>A parsed upload: what was detected and the columns with their first values and a suggested field.</summary>
public sealed record ImportSheetDto(Guid JobId, string FileName, int RowCount, string? Delimiter, string? Encoding, bool HasHeader, IReadOnlyList<ImportColumnDto> Columns);

/// <param name="Columns">The field of each column by index, null to ignore the column.</param>
/// <param name="Culture">Reads numbers, dates and enum labels of text files, e.g. de-DE; empty: the user's language.</param>
public sealed record ImportSettings(
    IReadOnlyList<string?> Columns, ImportMode Mode = ImportMode.Create, string? KeyField = null, string? Culture = null, ImportParseOptions? Parse = null, bool DryRun = false);

public sealed record ImportPreviewRowDto(int Row, string Action, IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> Errors);

/// <summary>
/// The first <see cref="Checked"/> of <see cref="Total"/> rows planned and validated (a dry run checks all of them):
/// <see cref="Rows"/> are the first of them, <see cref="Invalid"/> the first that fail.
/// </summary>
public sealed record ImportPreviewDto(int Total, int Checked, int Valid, int Creates, int Updates, IReadOnlyList<ImportPreviewRowDto> Rows, IReadOnlyList<ImportPreviewRowDto> Invalid);

/// <summary>A saved mapping of an entity set: column header to field, reused for files with the same headers.</summary>
public sealed record ImportProfileDto(Guid Id, string EntitySet, string Name, IReadOnlyDictionary<string, string> Columns, ImportMode Mode, string? KeyField, string? Culture);

public sealed record SaveImportProfileRequest(string Name, IReadOnlyDictionary<string, string> Columns, ImportMode Mode, string? KeyField, string? Culture);

public sealed record DataTransferJobDto(
    Guid Id,
    DataTransferKind Kind,
    string EntitySet,
    DataTransferStatus Status,
    bool DryRun,
    int Total,
    int Processed,
    int Created,
    int Updated,
    int Failed,
    string? FileName,
    string? ResultFileName,
    string? Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    bool CanUndo)
{
    public bool IsFinished => Status is DataTransferStatus.Completed or DataTransferStatus.Failed or DataTransferStatus.Undone or DataTransferStatus.Cancelled;
}
