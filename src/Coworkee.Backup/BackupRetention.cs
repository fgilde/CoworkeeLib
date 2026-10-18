using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Backup;

/// <summary>Old backups with their zip; the newest backup always stays, however old it is.</summary>
internal sealed class BackupRetention(CoworkeeDbContext db, IBlobStorage storage) : IRetentionTarget
{
    public string Key => "backups";

    public string Title => "Database backups";

    public string? Description => "Backups with their zip files in the blob storage; the newest backup always stays.";

    public int? DefaultDays => null;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public async Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var backup in await Older(before).OrderBy(b => b.CreatedAt).Select(b => new { b.Id, b.BlobKey }).ToListAsync(cancellationToken))
        {
            if (await db.Set<DatabaseBackup>().Where(b => b.Id == backup.Id).ExecuteDeleteAsync(cancellationToken) == 1)
            {
                await storage.DeleteAsync(backup.BlobKey, cancellationToken);
                total++;
            }
        }

        return total;
    }

    private IQueryable<DatabaseBackup> All => db.Set<DatabaseBackup>().IgnoreQueryFilters();

    private IQueryable<DatabaseBackup> Older(DateTimeOffset before) =>
        All.Where(b => b.CreatedAt < before && All.Any(newer => newer.CreatedAt > b.CreatedAt));
}
