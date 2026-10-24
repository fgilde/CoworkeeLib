using Coworkee.Contracts.Backup;

namespace Coworkee.Client.Blazor.Backup;

public interface IBackupApi
{
    Task<IReadOnlyList<BackupDto>> GetAsync(CancellationToken cancellationToken = default);

    Task<BackupDto> CreateAsync(CancellationToken cancellationToken = default);

    /// <summary>A downloaded backup zip, listed like a backup made here.</summary>
    Task<BackupDto> UploadAsync(string fileName, Stream content, CancellationToken cancellationToken = default);

    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<byte[]> DownloadAsync(Guid id, CancellationToken cancellationToken = default);
}
