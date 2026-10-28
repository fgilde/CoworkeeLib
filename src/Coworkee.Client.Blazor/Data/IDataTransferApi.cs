using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

/// <summary>Import wizard, exports and their jobs (api/v1/data).</summary>
public interface IDataTransferApi
{
    Task<IReadOnlyList<ImportTargetDto>> GetTargetsAsync(CancellationToken cancellationToken = default);

    Task<ImportTargetDto> GetTargetAsync(string entitySet, CancellationToken cancellationToken = default);

    Task<ImportSheetDto> UploadAsync(string entitySet, Stream content, string fileName, CancellationToken cancellationToken = default);

    Task<ImportSheetDto> ReadAgainAsync(Guid jobId, ImportParseOptions options, CancellationToken cancellationToken = default);

    Task<ImportPreviewDto> PreviewAsync(Guid jobId, ImportSettings settings, CancellationToken cancellationToken = default);

    Task<DataTransferJobDto> StartAsync(Guid jobId, ImportSettings settings, CancellationToken cancellationToken = default);

    Task<DataTransferJobDto> UndoAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportProfileDto>> GetProfilesAsync(string entitySet, CancellationToken cancellationToken = default);

    Task<ImportProfileDto> SaveProfileAsync(string entitySet, SaveImportProfileRequest request, CancellationToken cancellationToken = default);

    Task DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DataTransferJobDto>> GetJobsAsync(string? entitySet = null, CancellationToken cancellationToken = default);

    Task<DataTransferJobDto> GetJobAsync(Guid id, CancellationToken cancellationToken = default);

    Task<byte[]> DownloadAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>The file right away, or the job that writes it for large exports. The query is OData ($filter, $search, $orderby) plus columns.</summary>
    Task<ExportResult> ExportAsync(string entitySet, ExportFileFormat format, ODataQuery query, IReadOnlyCollection<string> columns, CancellationToken cancellationToken = default);

    /// <summary>The export's progress lines as they arrive; the first names the job, the last is ready (with the one-time download link, relative to the API), queued, failed or cancelled.</summary>
    IAsyncEnumerable<DataExportProgress> StreamExportAsync(string entitySet, string exportQuery, CancellationToken cancellationToken = default);

    /// <summary>Stops a running or queued export of the user: no file, no mail, and its slot is free. A finished job comes back as it is.</summary>
    Task<DataTransferJobDto> CancelJobAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Writes the export in the background and mails it to the user and the recipients (these need DataTransfer.ExportSchedules).</summary>
    Task<DataTransferJobDto> MailExportAsync(string entitySet, MailExportRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExportScheduleDto>> GetExportSchedulesAsync(string? entitySet = null, CancellationToken cancellationToken = default);

    /// <summary>Creates the schedule without <paramref name="id"/>, else changes it.</summary>
    Task<ExportScheduleDto> SaveExportScheduleAsync(Guid? id, SaveExportScheduleRequest request, CancellationToken cancellationToken = default);

    Task DeleteExportScheduleAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ExportScheduleDto> RunExportScheduleAsync(Guid id, CancellationToken cancellationToken = default);
}

public static class ExportQueries
{
    /// <summary>The query string of an export: the list's $filter, $search and $orderby, the format and the columns.</summary>
    public static string Of(ExportFileFormat format, ODataQuery query, IReadOnlyCollection<string> columns)
    {
        var url = (query with { Count = false, Top = null, Skip = null, Expand = null, Select = null }).ToQueryString();
        return url + (url.Contains('?') ? "&" : "?") + $"format={format.ToString().ToLowerInvariant()}"
               + (columns.Count == 0 ? string.Empty : $"&columns={Uri.EscapeDataString(string.Join(",", columns))}");
    }
}

/// <summary>Either <see cref="Content"/> to save or the <see cref="Job"/> that writes the file.</summary>
public sealed record ExportResult(byte[]? Content, string FileName, string ContentType, DataTransferJobDto? Job);
