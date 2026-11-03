namespace Coworkee.Contracts.Attachments;

/// <summary>A file attached to a record, in one of the record type's slots.</summary>
public sealed record RecordAttachmentDto(Guid Id, string EntityType, Guid EntityId, string Slot, string FileName, string ContentType, long Size, DateTimeOffset CreatedAt, Guid? CreatedBy);
