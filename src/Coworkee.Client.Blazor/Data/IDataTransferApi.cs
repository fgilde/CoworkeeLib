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
}

/// <summary>Either <see cref="Content"/> to save or the <see cref="Job"/> that writes the file.</summary>
public sealed record ExportResult(byte[]? Content, string FileName, string ContentType, DataTransferJobDto? Job);
