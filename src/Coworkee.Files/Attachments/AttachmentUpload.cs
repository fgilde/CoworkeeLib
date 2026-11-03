namespace Coworkee.Files.Attachments;

/// <summary>A file about to be attached, for <see cref="IAttachmentScanner"/>.</summary>
public sealed record AttachmentUpload(string EntityType, Guid EntityId, string Slot, string FileName, string ContentType, long Size);
