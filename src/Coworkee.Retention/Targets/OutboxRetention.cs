using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Retention.Targets;

internal sealed class OutboxRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "outbox";

    public string Title => "Processed outbox messages";

    public string? Description => "Domain events that were delivered; pending ones stay.";

    public int? DefaultDays => null;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(m => m.ProcessedAt).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<OutboxMessage> Older(DateTimeOffset before) =>
        db.Set<OutboxMessage>().IgnoreQueryFilters().Where(m => m.ProcessedAt != null && m.ProcessedAt < before);
}
