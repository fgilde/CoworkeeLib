using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coworkee.Files;

/// <summary>Soft deleted files with their blobs, then the soft deleted folders that hold nothing anymore, the deepest first.</summary>
internal sealed partial class DeletedFileRetention(CoworkeeDbContext db, IBlobStorage storage, ILogger<DeletedFileRetention> logger) : IRetentionTarget
{
    public string Key => "deleted-files";

    public string Title => "Deleted files";

    public string? Description => "Deleted files and folders, with the content of the files in the blob storage.";

    public int? DefaultDays => null;

    private IQueryable<StoredFile> AllFiles => db.Set<StoredFile>().IgnoreQueryFilters();

    private IQueryable<FileFolder> AllFolders => db.Set<FileFolder>().IgnoreQueryFilters();

    public async Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        await OlderFiles(before).LongCountAsync(cancellationToken) + await OlderFolders(before).LongCountAsync(cancellationToken);

    public async Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        long total = 0;
        var failed = new List<string>();
        List<(Guid Id, string BlobKey)> batch;
        do
        {
            batch = (await OlderFiles(before).OrderBy(f => f.DeletedAt).Take(BatchDeleteExtensions.BatchSize)
                .Select(f => new { f.Id, f.BlobKey }).ToListAsync(cancellationToken)).Select(f => (f.Id, f.BlobKey)).ToList();
            foreach (var (id, blobKey) in batch)
            {
                // a file restored right now keeps its content
                if (await OlderFiles(before).Where(f => f.Id == id).ExecuteDeleteAsync(cancellationToken) == 1)
                {
                    total++;
                    try
                    {
                        await storage.DeleteAsync(blobKey, cancellationToken);
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        LogBlobFailed(exception, blobKey);
                        failed.Add($"{blobKey}: {exception.Message}");
                    }
                }
            }
        }
        while (batch.Count == BatchDeleteExtensions.BatchSize);

        int folders;
        do
        {
            folders = await OlderFolders(before).Where(f => !AllFolders.Any(c => c.ParentId == f.Id) && !AllFiles.Any(x => x.FolderId == f.Id))
                .OrderBy(f => f.DeletedAt).Take(BatchDeleteExtensions.BatchSize).ExecuteDeleteAsync(cancellationToken);
            total += folders;
        }
        while (folders > 0);

        return failed.Count == 0
            ? total
            : throw new RetentionIncompleteException(total, $"{failed.Count} blobs could not be deleted:{Environment.NewLine}{string.Join(Environment.NewLine, failed.Take(20))}");
    }

    private IQueryable<StoredFile> OlderFiles(DateTimeOffset before) => AllFiles.Where(f => f.IsDeleted && f.DeletedAt < before);

    private IQueryable<FileFolder> OlderFolders(DateTimeOffset before) => AllFolders.Where(f => f.IsDeleted && f.DeletedAt < before);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The blob {BlobKey} of a deleted file could not be deleted.")]
    private partial void LogBlobFailed(Exception exception, string blobKey);
}
