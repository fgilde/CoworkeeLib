namespace Coworkee.Contracts.Attachments;

/// <summary>A file attached to a record, in one of the record type's slots.</summary>
public sealed record RecordAttachmentDto(Guid Id, string EntityType, Guid EntityId, string Slot, string FileName, string ContentType, long Size, DateTimeOffset CreatedAt, Guid? CreatedBy);

/// <summary>A slot of a record with its files, its limits and whether the current user may change it.</summary>
/// <param name="ContentTypes">Accepted types such as <c>application/pdf</c> or <c>image/*</c>; empty takes any.</param>
/// <param name="Multiple">False: an upload replaces the file of the slot.</param>
public sealed record AttachmentSlotDto(string Slot, long MaxBytes, IReadOnlyList<string> ContentTypes, bool Multiple, bool CanWrite, IReadOnlyList<RecordAttachmentDto> Items);

public static class AttachmentTopics
{
    /// <summary>Realtime topic that signals every added, replaced or deleted attachment of one record.</summary>
    public static string Record(string entityType, Guid entityId) => $"attachments:{entityType}:{entityId}";
}

public static class AttachmentUrls
{
    public const string Root = "api/v1/attachments";

    public static string Slot(string entityType, Guid entityId, string slot) =>
        $"{Root}/{Uri.EscapeDataString(entityType)}/{entityId}/{Uri.EscapeDataString(slot)}";

    /// <summary>The content, inline; <paramref name="download"/> asks the browser to save it.</summary>
    public static string Content(RecordAttachmentDto attachment, bool download = false) =>
        $"{Slot(attachment.EntityType, attachment.EntityId, attachment.Slot)}/{attachment.Id}" + (download ? "?download=true" : string.Empty);
}
