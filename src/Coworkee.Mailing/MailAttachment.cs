namespace Coworkee.Mailing;

/// <summary>A file sent with a mail.</summary>
public sealed record MailAttachment(string FileName, string ContentType, byte[] Content)
{
    public const int MaxTotalBytes = 10 * 1024 * 1024;
}
