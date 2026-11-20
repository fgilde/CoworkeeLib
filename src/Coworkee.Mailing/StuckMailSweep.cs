using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Mailing;

/// <summary>Ends mails left in <see cref="OutgoingMailStatus.Sending"/> past <see cref="MailOptions.SendingTimeout"/> as <see cref="MailOptions.RetryStuckSending"/> says.</summary>
internal sealed class StuckMailSweep(CoworkeeDbContext db, IBackgroundJobs jobs, TimeProvider clock, IOptions<MailOptions> options) : IRecurringJob
{
    public const string Id = "coworkee-mail-stuck-sending";
    public const string Cron = "*/5 * * * *";
    public const string Message = "Delivery unknown: sending stopped before the result was saved, the mail may have been sent. It is not sent again.";
    public const string RetryMessage = "Sending stopped before the result was saved; queued again (the mail may arrive twice).";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var before = clock.GetUtcNow() - options.Value.SendingTimeout;
        // mails taken into Sending before SendingSince existed count from when they were queued
        var stuck = db.Set<OutgoingMail>().IgnoreQueryFilters().Where(m => m.Status == OutgoingMailStatus.Sending && (m.SendingSince ?? m.QueuedAt) < before);
        if (!options.Value.RetryStuckSending)
        {
            await stuck.ExecuteUpdateAsync(update => update.SetProperty(m => m.Status, OutgoingMailStatus.Failed).SetProperty(m => m.LastError, Message), cancellationToken);
            return;
        }

        foreach (var (id, tenantId) in (await stuck.Select(m => new { m.Id, m.TenantId }).ToListAsync(cancellationToken)).Select(m => (m.Id, m.TenantId)))
        {
            // a sender that finished meanwhile keeps its result
            if (await db.Set<OutgoingMail>().IgnoreQueryFilters().Where(m => m.Id == id && m.Status == OutgoingMailStatus.Sending)
                    .ExecuteUpdateAsync(update => update.SetProperty(m => m.Status, OutgoingMailStatus.Queued).SetProperty(m => m.LastError, RetryMessage), cancellationToken) == 1)
            {
                // the sweep runs for every organisation: the job sends in the mail's own one, with its mail connection (not the system's)
                using (CurrentUserScope.BeginSystem(tenantId))
                {
                    jobs.Enqueue<SendMailJob, Guid>(id, "mail");
                }
            }
        }
    }
}
