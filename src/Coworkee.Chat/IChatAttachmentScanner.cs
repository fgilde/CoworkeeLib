namespace Coworkee.Chat;

/// <summary>Checks every chat upload before it is stored, e.g. with a virus scanner; one that says no rejects the file.</summary>
public interface IChatAttachmentScanner
{
    /// <param name="content">Seekable, at its start; leave it anywhere.</param>
    Task<bool> IsCleanAsync(Stream content, string name, string contentType, CancellationToken cancellationToken);
}
