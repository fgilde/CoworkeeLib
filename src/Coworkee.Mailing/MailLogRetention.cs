using Coworkee.Application.Retention;
using Coworkee.Contracts.Mailing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

internal sealed class MailLogRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "mail-log";

    public string Title => "Mail log";

    public string? Description => "Sent, failed and skipped mails with their content; mails still waiting stay.";

    public int? DefaultDays => null;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(m => m.QueuedAt).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<OutgoingMail> Older(DateTimeOffset before) => db.Set<OutgoingMail>().IgnoreQueryFilters()
        .Where(m => m.QueuedAt < before && m.Status != OutgoingMailStatus.Queued && m.Status != OutgoingMailStatus.Sending);
}
