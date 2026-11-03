using Coworkee.Contracts.Attachments;

namespace Coworkee.Files.Attachments;

/// <summary>A stored attachment opened for reading; the caller disposes <see cref="Content"/>.</summary>
public sealed record RecordAttachmentFile(RecordAttachmentDto Attachment, Stream Content);
