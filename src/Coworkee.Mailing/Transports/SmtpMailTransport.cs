using System.Globalization;
using Coworkee.Contracts.Mailing;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Coworkee.Mailing;

internal sealed class SmtpMailTransport : IMailTransport
{
    public string Provider => MailProviders.Smtp;

    public async Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken)
    {
        var message = new MimeMessage { Subject = mail.Subject, Body = new BodyBuilder { HtmlBody = mail.HtmlBody }.ToMessageBody() };
        message.From.Add(MailboxAddress.Parse(mail.From));
        message.To.Add(MailboxAddress.Parse(mail.To));

        var port = int.Parse(connection[MailSettings.Port] ?? "25", CultureInfo.InvariantCulture);
        var useSsl = bool.TryParse(connection[MailSettings.UseSsl], out var ssl) && ssl;
        using var client = new SmtpClient();
        await client.ConnectAsync(connection[MailSettings.Host]!, port, useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
        if (connection[MailSettings.UserName] is { Length: > 0 } userName)
        {
            await client.AuthenticateAsync(userName, connection[MailSettings.Password] ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
