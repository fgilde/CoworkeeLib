namespace Coworkee.Mailing;

/// <summary>A file of a queued mail; deleted with the mail.</summary>
public sealed class OutgoingMailAttachment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MailId { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public required byte[] Content { get; set; }
}
