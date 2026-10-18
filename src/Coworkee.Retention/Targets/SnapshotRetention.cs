using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Infrastructure.Versioning;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Retention.Targets;

/// <summary>Revisions older than the day, except the newest one of each entity.</summary>
internal sealed class SnapshotRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "snapshots";

    public string Title => "Entity snapshots";

    public string? Description => "Older revisions of versioned entities such as mail templates; their version history and restore read them. The newest revision of every entity stays.";

    public int? DefaultDays => null;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(s => s.CreatedAt).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<EntitySnapshot> All => db.Set<EntitySnapshot>().IgnoreQueryFilters();

    private IQueryable<EntitySnapshot> Older(DateTimeOffset before) =>
        All.Where(s => s.CreatedAt < before && All.Any(n => n.EntityType == s.EntityType && n.EntityId == s.EntityId && n.Revision > s.Revision));
}
