using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Mailing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Mailing;

internal sealed class SendMailJob(CoworkeeDbContext db, MailConnectionResolver resolver, IEnumerable<IMailTransport> transports, TimeProvider clock, IOptions<BackgroundJobOptions> options)
    : IBackgroundJob<Guid>
{
    public async Task ExecuteAsync(Guid mailId, CancellationToken cancellationToken)
    {
        var claimed = await db.Set<OutgoingMail>()
            .Where(m => m.Id == mailId && m.Status == OutgoingMailStatus.Queued)
            .ExecuteUpdateAsync(update => update
                .SetProperty(m => m.Status, OutgoingMailStatus.Sending)
                .SetProperty(m => m.SendingSince, clock.GetUtcNow())
                .SetProperty(m => m.Attempts, m => m.Attempts + 1), cancellationToken);
        if (claimed == 0)
        {
            return;
        }

        var mail = await db.Set<OutgoingMail>().SingleAsync(m => m.Id == mailId, cancellationToken);

        try
        {
            var connection = await resolver.ResolveAsync(cancellationToken);
            if (!MailWhitelist.Allows(connection[MailSettings.Whitelist], mail.To))
            {
                mail.Status = OutgoingMailStatus.Skipped;
                mail.LastError = "Recipient is not on the whitelist.";
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            var transport = transports.FirstOrDefault(t => t.Provider == connection.Provider)
                ?? throw new InvalidOperationException($"No mail transport is registered for provider '{connection.Provider}'.");
            var attachments = await db.Set<OutgoingMailAttachment>().AsNoTracking().Where(a => a.MailId == mail.Id).OrderBy(a => a.FileName)
                .Select(a => new MailAttachment(a.FileName, a.ContentType, a.Content)).ToListAsync(cancellationToken);
            await transport.SendAsync(new MailEnvelope(connection[MailSettings.From] ?? "noreply@localhost", mail.To, mail.Subject, mail.HtmlBody, attachments), connection, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            mail.LastError = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
            mail.Status = mail.Attempts >= options.Value.Attempts ? OutgoingMailStatus.Failed : OutgoingMailStatus.Queued;
            await db.SaveChangesAsync(CancellationToken.None);
            if (mail.Status == OutgoingMailStatus.Queued)
            {
                throw;
            }

            return;
        }

        // a crash between the provider and this write leaves the mail in Sending; StuckMailSweep ends it
        mail.Status = OutgoingMailStatus.Sent;
        mail.SentAt = clock.GetUtcNow();
        mail.LastError = null;
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
