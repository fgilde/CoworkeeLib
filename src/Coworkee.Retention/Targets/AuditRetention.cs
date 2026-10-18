using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Retention.Targets;

internal sealed class AuditRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "audit";

    public string Title => "Audit trail";

    public string? Description => "Who changed what. Entity history and restore read it: older changes can no longer be shown or undone.";

    public int? DefaultDays => null;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(e => e.OccurredAt).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<AuditEntry> Older(DateTimeOffset before) => db.Set<AuditEntry>().IgnoreQueryFilters().Where(e => e.OccurredAt < before);
}
