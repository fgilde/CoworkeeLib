using Coworkee.BackgroundJobs;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Chat;

/// <summary>Deletes files that were uploaded but never sent, with their pictures, once <see cref="ChatOptions.UnsentAttachmentLifetime"/> is over.</summary>
internal sealed class ChatAttachmentCleanup(CoworkeeDbContext db, IBlobStorage storage, TimeProvider clock, IOptions<ChatOptions> options) : IRecurringJob
{
    public const string Id = "coworkee-chat-attachment-cleanup";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var before = clock.GetUtcNow() - options.Value.UnsentAttachmentLifetime;
        var unsent = await db.Set<ChatAttachment>().IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.MessageId == null && a.UploadedAt < before)
            .Select(a => new { a.Id, a.BlobKey, a.ThumbnailKey })
            .ToListAsync(cancellationToken);
        foreach (var attachment in unsent)
        {
            // a message sent right now keeps its file
            if (await db.Set<ChatAttachment>().IgnoreQueryFilters().Where(a => a.Id == attachment.Id && a.MessageId == null).ExecuteDeleteAsync(cancellationToken) == 1)
            {
                await ChatAttachmentBlobs.DeleteAsync(storage, attachment.BlobKey, attachment.ThumbnailKey, cancellationToken);
            }
        }
    }
}

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
