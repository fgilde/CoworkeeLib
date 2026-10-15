using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

public static class DataTransferPermissions
{
    public const string GroupName = "DataTransfer";

    /// <summary>Opens the import and export page; each set still needs its own permissions.</summary>
    public const string Use = "DataTransfer.Use";
}

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
    public bool IsFinished => Status is DataTransferStatus.Completed or DataTransferStatus.Failed or DataTransferStatus.Undone;
}
