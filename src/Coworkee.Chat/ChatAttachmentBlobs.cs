using Coworkee.Storage;

namespace Coworkee.Chat;

internal static class ChatAttachmentBlobs
{
    public static async Task DeleteAsync(IBlobStorage storage, string blobKey, string? thumbnailKey, CancellationToken cancellationToken)
    {
        await storage.DeleteAsync(blobKey, cancellationToken);
        if (thumbnailKey is not null)
        {
            await storage.DeleteAsync(thumbnailKey, cancellationToken);
        }
    }
}
