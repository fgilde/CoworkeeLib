namespace Coworkee.Contracts.Attachments;

/// <summary>A slot of a record with its files, its limits and whether the current user may change it.</summary>
/// <param name="ContentTypes">Accepted types such as <c>application/pdf</c> or <c>image/*</c>; empty takes any.</param>
/// <param name="Multiple">False: an upload replaces the file of the slot.</param>
public sealed record AttachmentSlotDto(string Slot, long MaxBytes, IReadOnlyList<string> ContentTypes, bool Multiple, bool CanWrite, IReadOnlyList<RecordAttachmentDto> Items);
