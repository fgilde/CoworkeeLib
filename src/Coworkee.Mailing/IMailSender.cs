namespace Coworkee.Mailing;

public interface IMailSender
{
    Task<Guid> QueueAsync(string to, string template, object model, string? culture, Guid? campaignId, CancellationToken cancellationToken);

    Task<Guid> QueueAsync(string to, string template, object model, string? culture, CancellationToken cancellationToken) =>
        QueueAsync(to, template, model, culture, null, cancellationToken);

    /// <summary>A mail with files attached (together at most <see cref="MailAttachment.MaxTotalBytes"/>); they stay with the mail until the mail log is cleaned up.</summary>
    Task<Guid> QueueWithAttachmentsAsync(string to, string template, object model, string? culture, IReadOnlyList<MailAttachment> attachments, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"{GetType().Name} sends no attachments.");
}
