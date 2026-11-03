namespace Coworkee.Mailing;

public sealed record MailEnvelope(string From, string To, string Subject, string HtmlBody, IReadOnlyList<MailAttachment>? Attachments = null)
{
    public IReadOnlyList<MailAttachment> Files => Attachments ?? [];
}
