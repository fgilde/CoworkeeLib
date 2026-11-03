namespace Coworkee.Files.Attachments;

/// <summary>What is about to happen on the attachments of one record: read (list, download) or write (upload, delete).</summary>
public sealed record AttachmentAccess(string EntityType, Guid EntityId, string Slot, bool Write);
