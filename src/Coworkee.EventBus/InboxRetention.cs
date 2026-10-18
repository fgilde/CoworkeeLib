using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.EventBus;

internal sealed class InboxRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "inbox";

    public string Title => "Processed integration events";

    public string? Description => "The inbox that skips redeliveries; keep it longer than a transport redelivers.";

    public int? DefaultDays => null;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(m => m.ProcessedAt).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<InboxMessage> Older(DateTimeOffset before) => db.Set<InboxMessage>().IgnoreQueryFilters().Where(m => m.ProcessedAt < before);
}
