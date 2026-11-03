using Coworkee.Domain;

namespace Coworkee.Chat;

/// <summary>A file of a message; uploaded first (no <see cref="MessageId"/>), attached when the message is sent.</summary>
public sealed class ChatAttachment : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid ConversationId { get; set; }

    public Guid? MessageId { get; set; }

    public Guid UploadedBy { get; set; }

    public required string Name { get; set; }

    public required string ContentType { get; set; }

    public long Size { get; set; }

    public required string BlobKey { get; set; }

    /// <summary>A small WebP of a picture, next to <see cref="BlobKey"/>; null for other files and pictures the server cannot read.</summary>
    public string? ThumbnailKey { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
}
