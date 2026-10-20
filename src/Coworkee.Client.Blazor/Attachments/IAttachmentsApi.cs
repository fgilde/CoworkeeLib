using Coworkee.Contracts.Attachments;

namespace Coworkee.Client.Blazor.Attachments;

/// <summary>The files of records registered with AddRecordAttachments; <see cref="AttachmentUrls.Content"/> is the address of one file.</summary>
public interface IAttachmentsApi
{
    /// <summary>Every slot of the record the user may read.</summary>
    Task<IReadOnlyList<AttachmentSlotDto>> GetAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    Task<AttachmentSlotDto> GetSlotAsync(string entityType, Guid entityId, string slot, CancellationToken cancellationToken = default);

    /// <summary>Sends <paramref name="content"/> as the request body; in a single-file slot it replaces the file there.</summary>
    Task<RecordAttachmentDto> UploadAsync(string entityType, Guid entityId, string slot, string fileName, string? contentType, Stream content, CancellationToken cancellationToken = default);

    Task DeleteAsync(RecordAttachmentDto attachment, CancellationToken cancellationToken = default);
}
