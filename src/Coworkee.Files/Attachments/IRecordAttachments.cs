using Coworkee.Contracts.Attachments;
using Coworkee.Core.Results;

namespace Coworkee.Files.Attachments;

/// <summary>
/// Files of records in named slots: metadata in <c>cw.RecordAttachments</c>, content in the blob storage.
/// Checks the slot's limits but not who may do it: app code calls it after its own checks, the endpoints after the record's visibility.
/// Changes join the caller's unit of work (the pipeline saves after the command); a blob written for a row that is never committed is deleted when the scope ends.
/// </summary>
public interface IRecordAttachments
{
    /// <summary>Writes the file to the blob storage and adds its row; in a single-file slot it replaces the one there. Saved with the unit of work.</summary>
    Task<Result<RecordAttachmentDto>> PutAsync(string entityType, Guid entityId, string slot, string fileName, string? contentType, Stream content, CancellationToken cancellationToken);

    /// <summary>The attachment with its content, or null when it (or its blob) is gone.</summary>
    Task<RecordAttachmentFile?> OpenAsync(Guid attachmentId, CancellationToken cancellationToken);

    /// <summary>Removes the row; the blob goes after the commit.</summary>
    Task<bool> DeleteAsync(Guid attachmentId, CancellationToken cancellationToken);

    /// <summary>Every attachment of the record, e.g. when the app deletes it for good.</summary>
    Task DeleteAllAsync(string entityType, Guid entityId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecordAttachmentDto>> ListAsync(string entityType, Guid entityId, string? slot = null, CancellationToken cancellationToken = default);
}
