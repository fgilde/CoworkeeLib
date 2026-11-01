using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Mailing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Mailing;

public sealed class MailOptions
{
    public const string Section = "Coworkee:Mail";

    /// <summary>A mail in <see cref="OutgoingMailStatus.Sending"/> for longer than this was left by a sender that stopped (a crash, a restart).</summary>
    public TimeSpan SendingTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// False (default): such a mail fails with <see cref="StuckMailSweep.Message"/>, since the provider may have sent it. True: it is queued again,
    /// which sends it twice when the provider had sent it.
    /// </summary>
    public bool RetryStuckSending { get; set; }
}

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

        foreach (var id in await stuck.Select(m => m.Id).ToListAsync(cancellationToken))
        {
            // a sender that finished meanwhile keeps its result
            if (await db.Set<OutgoingMail>().IgnoreQueryFilters().Where(m => m.Id == id && m.Status == OutgoingMailStatus.Sending)
                    .ExecuteUpdateAsync(update => update.SetProperty(m => m.Status, OutgoingMailStatus.Queued).SetProperty(m => m.LastError, RetryMessage), cancellationToken) == 1)
            {
                jobs.Enqueue<SendMailJob, Guid>(id, "mail");
            }
        }
    }
}
