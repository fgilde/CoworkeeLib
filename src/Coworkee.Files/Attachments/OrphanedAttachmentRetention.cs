using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files.Attachments;

/// <summary>
/// Attachments whose record is gone for good, with their blobs. A soft deleted record keeps its files (a restore brings them back)
/// until the "soft-deleted" target or the app removes the row.
/// </summary>
internal sealed class OrphanedAttachmentRetention(CoworkeeDbContext db, RecordAttachmentOptions options, IBlobStorage storage) : IRetentionTarget
{
    public string Key => "orphaned-attachments";

    public string Title => "Attachments of deleted records";

    public string? Description => "Files attached to records that no longer exist, with their content in the blob storage.";

    // a day guards the files of a record that is still being created
    public int? DefaultDays => 1;

    public async Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var type in Local())
        {
            total += await Orphans(type, before).LongCountAsync(cancellationToken);
        }

        return total;
    }

    public async Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        long total = 0;
        var failed = new List<string>();
        foreach (var type in Local())
        {
            List<(Guid Id, string BlobKey)> batch;
            do
            {
                batch = (await Orphans(type, before).OrderBy(a => a.CreatedAt).Take(BatchDeleteExtensions.BatchSize)
                    .Select(a => new { a.Id, a.BlobKey }).ToListAsync(cancellationToken)).Select(a => (a.Id, a.BlobKey)).ToList();
                foreach (var (id, blobKey) in batch)
                {
                    if (await db.Set<RecordAttachment>().IgnoreQueryFilters().Where(a => a.Id == id).ExecuteDeleteAsync(cancellationToken) == 1)
                    {
                        total++;
                        try
                        {
                            await storage.DeleteAsync(blobKey, cancellationToken);
                        }
                        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                        {
                            failed.Add($"{blobKey}: {exception.Message}");
                        }
                    }
                }
            }
            while (batch.Count == BatchDeleteExtensions.BatchSize);
        }

        return failed.Count == 0
            ? total
            : throw new RetentionIncompleteException(total, $"{failed.Count} blobs could not be deleted:{Environment.NewLine}{string.Join(Environment.NewLine, failed.Take(20))}");
    }

    // ponytail: attachments of records in an external database are never cleaned up; one query cannot span two databases, compare ids in batches if they pile up
    private IEnumerable<RecordAttachmentType> Local() => options.Types.Values.Where(t => t.ExistingIds(db) is not null);

    private IQueryable<RecordAttachment> Orphans(RecordAttachmentType type, DateTimeOffset before)
    {
        var existing = type.ExistingIds(db)!;
        return db.Set<RecordAttachment>().IgnoreQueryFilters().Where(a => a.EntityType == type.EntityType && a.CreatedAt < before && !existing.Contains(a.EntityId));
    }
}
