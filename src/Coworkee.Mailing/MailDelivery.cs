using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Coworkee.Mailing;

public interface IMailSender
{
    Task<Guid> QueueAsync(string to, string template, object model, string? culture, CancellationToken cancellationToken);
}

internal sealed class MailSender(CoworkeeDbContext db, IMailTemplateRenderer renderer, ICurrentUser currentUser, TimeProvider clock) : IMailSender
{
    public async Task<Guid> QueueAsync(string to, string template, object model, string? culture, CancellationToken cancellationToken)
    {
        var rendered = await renderer.RenderAsync(template, model, culture, cancellationToken);
        var mail = OutgoingMail.Queue(to, rendered, template, currentUser.TenantId, clock.GetUtcNow());
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

public sealed record SmtpSettings(string Host, int Port, bool UseSsl, string? UserName, string? Password, string From);

public interface ISmtpTransport
{
    Task SendAsync(SmtpSettings settings, MimeMessage message, CancellationToken cancellationToken);
}

internal sealed class MailKitSmtpTransport : ISmtpTransport
{
    public async Task SendAsync(SmtpSettings settings, MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
        if (!string.IsNullOrEmpty(settings.UserName))
        {
            await client.AuthenticateAsync(settings.UserName, settings.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}

internal sealed class SendMailJob(CoworkeeDbContext db, ISettingProvider settings, ISmtpTransport transport, TimeProvider clock, IOptions<BackgroundJobOptions> options)
    : IBackgroundJob<Guid>
{
    public async Task ExecuteAsync(Guid mailId, CancellationToken cancellationToken)
    {
        var mail = await db.Set<OutgoingMail>().SingleOrDefaultAsync(m => m.Id == mailId, cancellationToken);
        if (mail is null || mail.Status != OutgoingMailStatus.Queued)
        {
            return;
        }

        if (!MailWhitelist.Allows(await settings.GetAsync(MailSettings.Whitelist, cancellationToken), mail.To))
        {
            mail.Status = OutgoingMailStatus.Skipped;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        mail.Attempts++;
        try
        {
            var smtp = await SmtpSettingsAsync(cancellationToken);
            var message = new MimeMessage { Subject = mail.Subject, Body = new BodyBuilder { HtmlBody = mail.HtmlBody }.ToMessageBody() };
            message.From.Add(MailboxAddress.Parse(smtp.From));
            message.To.Add(MailboxAddress.Parse(mail.To));
            await transport.SendAsync(smtp, message, cancellationToken);
            mail.Status = OutgoingMailStatus.Sent;
            mail.SentAt = clock.GetUtcNow();
            mail.LastError = null;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            mail.LastError = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
            var final = mail.Attempts >= options.Value.Attempts;
            if (final)
            {
                mail.Status = OutgoingMailStatus.Failed;
            }

            await db.SaveChangesAsync(CancellationToken.None);
            if (!final)
            {
                throw;
            }
        }
    }

    private async Task<SmtpSettings> SmtpSettingsAsync(CancellationToken cancellationToken)
    {
        var host = await settings.GetAsync(MailSettings.Host, cancellationToken);
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("SMTP host is not configured.");
        }

        return new SmtpSettings(
            host,
            await settings.GetAsync<int>(MailSettings.Port, cancellationToken),
            await settings.GetAsync<bool>(MailSettings.UseSsl, cancellationToken),
            await settings.GetAsync(MailSettings.UserName, cancellationToken),
            await settings.GetAsync(MailSettings.Password, cancellationToken),
            await settings.GetAsync(MailSettings.From, cancellationToken) ?? "noreply@localhost");
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
