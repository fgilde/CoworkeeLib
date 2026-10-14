using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Mailing;

public interface IMailSender
{
    Task<Guid> QueueAsync(string to, string template, object model, string? culture, Guid? campaignId, CancellationToken cancellationToken);

    Task<Guid> QueueAsync(string to, string template, object model, string? culture, CancellationToken cancellationToken) =>
        QueueAsync(to, template, model, culture, null, cancellationToken);
}

internal sealed class MailSender(CoworkeeDbContext db, IMailTemplateRenderer renderer, ICurrentUser currentUser, TimeProvider clock) : IMailSender
{
    public async Task<Guid> QueueAsync(string to, string template, object model, string? culture, Guid? campaignId, CancellationToken cancellationToken)
    {
        var rendered = await renderer.RenderAsync(template, model, culture, cancellationToken);
        var mail = OutgoingMail.Queue(to, rendered, template, currentUser.TenantId, clock.GetUtcNow(), campaignId);
        db.Add(mail);
        return mail.Id;
    }
}

internal sealed class MailQueuedHandler(IBackgroundJobs jobs) : IDomainEventHandler<MailQueued>
{
    public Task HandleAsync(MailQueued domainEvent, CancellationToken cancellationToken)
    {
        jobs.Enqueue<SendMailJob, Guid>(domainEvent.MailId, "mail");
        return Task.CompletedTask;
    }
}

public sealed record MailEnvelope(string From, string To, string Subject, string HtmlBody);

public sealed record MailConnection(string Provider, IReadOnlyDictionary<string, string?> Values)
{
    public string? this[string name] => Values.GetValueOrDefault(name);
}

public interface IMailTransport
{
    string Provider { get; }

    Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken);
}

internal sealed class SendMailJob(CoworkeeDbContext db, MailConnectionResolver resolver, IEnumerable<IMailTransport> transports, TimeProvider clock, IOptions<BackgroundJobOptions> options)
    : IBackgroundJob<Guid>
{
    public async Task ExecuteAsync(Guid mailId, CancellationToken cancellationToken)
    {
        var claimed = await db.Set<OutgoingMail>()
            .Where(m => m.Id == mailId && m.Status == OutgoingMailStatus.Queued)
            .ExecuteUpdateAsync(update => update
                .SetProperty(m => m.Status, OutgoingMailStatus.Sending)
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
            await transport.SendAsync(new MailEnvelope(connection[MailSettings.From] ?? "noreply@localhost", mail.To, mail.Subject, mail.HtmlBody), connection, cancellationToken);
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

        // ponytail: a crash between SMTP and this write leaves the mail in Sending (never resent); add a reconciler if that shows up
        mail.Status = OutgoingMailStatus.Sent;
        mail.SentAt = clock.GetUtcNow();
        mail.LastError = null;
        await db.SaveChangesAsync(CancellationToken.None);
    }
}

public static class MailWhitelist
{
    public static bool Allows(string? whitelist, string address)
    {
        var entries = (whitelist ?? string.Empty).Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return entries.Length == 0 || entries.Any(entry => entry.StartsWith('@')
            ? address.EndsWith(entry, StringComparison.OrdinalIgnoreCase)
            : string.Equals(entry, address, StringComparison.OrdinalIgnoreCase));
    }
}
