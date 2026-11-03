namespace Coworkee.Contracts.Chat;

public sealed record ChatAttachmentDto(Guid Id, string Name, string ContentType, long Size)
{
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>A small picture is at <c>api/v1/chat/attachments/{id}/thumbnail</c>.</summary>
    public bool HasThumbnail { get; init; }
}
