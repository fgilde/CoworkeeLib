using Coworkee.Application;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Notifications;

/// <summary>Every minute: the waiting notification mails whose delay is over.</summary>
internal sealed class NotificationMailJob(CoworkeeDbContext db, NotificationMails mails, IDistributedLock locks, TimeProvider clock) : IRecurringJob
{
    public const string Id = "coworkee-notification-mails";
    public const string Cron = "* * * * *";

    // ponytail: at most this many mails per run; the rest follow a minute later
    private const int Batch = 500;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var held = await locks.AcquireAsync(Id, TimeSpan.Zero, cancellationToken);
        if (held is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var due = await db.Set<PendingNotificationMail>().AsNoTracking().Where(m => m.DueAt <= now).OrderBy(m => m.DueAt).Take(Batch).ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return;
        }

        // the claims and the queued mails commit together: a mail is neither lost nor sent twice
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var claimed = new List<PendingNotificationMail>();
        foreach (var mail in due)
        {
            if (await ClaimAsync(mail, cancellationToken))
            {
                claimed.Add(mail);
            }
        }

        // ponytail: one mail per notification through the global mail settings, as the digest; templates and the mail log follow the mail's tenant
        foreach (var tenant in claimed.GroupBy(m => m.TenantId))
        {
            using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenant.Key));
            await mails.QueueAsync([.. tenant.Select(m => new NotificationMails.Item(m.UserId, m.TenantId, m.Type, m.Title, m.Body, m.Arguments, m.Link, m.MailModel))], cancellationToken);
        }

        // the MailQueued outbox rows of these mails get the tenant of this save, which is none: harmless today, because their handler only enqueues the send
        // job by mail id, and that job reads the mail by id and sends through the global mail settings
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Takes the mail as it was read; false when a publisher replaced it meanwhile (it waits anew) or another run took it.</summary>
    internal async Task<bool> ClaimAsync(PendingNotificationMail seen, CancellationToken cancellationToken) =>
        await db.Set<PendingNotificationMail>().Where(m => m.Id == seen.Id && m.Version == seen.Version).ExecuteDeleteAsync(cancellationToken) == 1;
}
