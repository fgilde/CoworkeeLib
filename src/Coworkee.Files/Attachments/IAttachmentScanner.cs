namespace Coworkee.Files.Attachments;

/// <summary>
/// A hook before an upload is stored, e.g. a virus scanner; returns why the file is refused, or null. Checks record attachments and uploads
/// to the files (<see cref="FileUploads"/>).
/// </summary>
public interface IAttachmentScanner
{
    Task<string?> ScanAsync(AttachmentUpload upload, Stream content, CancellationToken cancellationToken);
}
