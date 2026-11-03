namespace Coworkee.Mailing;

public interface IMailTransport
{
    string Provider { get; }

    Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken);
}
