using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Infrastructure.Versioning;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Retention.Targets;

internal sealed class SnapshotRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "snapshots";

    public string Title => "Entity snapshots";

    public string? Description => "The stored revisions of versioned entities.";

    public int? DefaultDays => null;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(s => s.CreatedAt).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<EntitySnapshot> Older(DateTimeOffset before) => db.Set<EntitySnapshot>().IgnoreQueryFilters().Where(s => s.CreatedAt < before);
}
