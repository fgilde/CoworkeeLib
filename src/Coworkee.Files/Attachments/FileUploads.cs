namespace Coworkee.Files.Attachments;

/// <summary>Names an upload to the files (the Files page, <c>POST /api/v1/files</c>) gets in <see cref="AttachmentUpload"/>.</summary>
public static class FileUploads
{
    /// <summary><see cref="AttachmentUpload.EntityType"/> of a file upload; <see cref="AttachmentUpload.EntityId"/> is its folder (<see cref="Guid.Empty"/> for the root).</summary>
    public const string EntityType = "StoredFile";

    /// <summary><see cref="AttachmentUpload.Slot"/> of a file upload.</summary>
    public const string Slot = "files";
}
