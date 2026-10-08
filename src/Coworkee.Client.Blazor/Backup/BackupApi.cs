using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Backup;

namespace Coworkee.Client.Blazor.Backup;

internal sealed class BackupApi(HttpClient http) : ApiClientBase(http), IBackupApi
{
    private const string Root = "api/v1/backups";

    public async Task<IReadOnlyList<BackupDto>> GetAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<BackupDto[]>(Root, cancellationToken);

    public Task<BackupDto> CreateAsync(CancellationToken cancellationToken = default) =>
        SendAsync<BackupDto>(HttpMethod.Post, Root, null, cancellationToken);

    public Task RestoreAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Root}/{id}/restore", null, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Root}/{id}", null, cancellationToken);

    public async Task<byte[]> DownloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await SendContentAsync(HttpMethod.Get, $"{Root}/{id}/download", null, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
