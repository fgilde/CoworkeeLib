using System.Net.Http.Headers;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Chat;

/// <summary>Checks every chat upload before it is stored, e.g. with a virus scanner; one that says no rejects the file.</summary>
public interface IChatAttachmentScanner
{
    /// <param name="content">Seekable, at its start; leave it anywhere.</param>
    Task<bool> IsCleanAsync(Stream content, string name, string contentType, CancellationToken cancellationToken);
}

internal sealed class ChatAttachmentHandlers(
    CoworkeeDbContext db, ChatViews views, IBlobStorage storage, IEnumerable<IChatAttachmentScanner> scanners, IServiceProvider services, TimeProvider clock)
    : IHandler<UploadChatAttachmentCommand, Result<ChatAttachmentDto>>,
      IHandler<OpenChatAttachmentQuery, Result<ChatAttachmentContent>>
{
    private const string FallbackContentType = "application/octet-stream";

    public async Task<Result<ChatAttachmentDto>> HandleAsync(UploadChatAttachmentCommand command, CancellationToken cancellationToken)
    {
        if (await views.FindAsync(command.ConversationId, cancellationToken) is null)
        {
            return ChatErrors.NotFound;
        }

        if (CleanName(command.Name) is not { } name)
        {
            return ChatErrors.InvalidName;
        }

        var limit = await MaxBytesAsync(cancellationToken);
        // ponytail: buffered to a temp file so the scanners and every storage provider get a seekable stream of known length
        await using var buffer = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        switch (await CopyAsync(command.Content, buffer, limit, cancellationToken))
        {
            case < 0:
                return ChatErrors.TooLarge;
            case 0:
                return ChatErrors.Empty;
        }

        var contentType = MediaTypeHeaderValue.TryParse(command.ContentType, out var parsed) && parsed.MediaType is { Length: <= 200 } mediaType ? mediaType : FallbackContentType;
        foreach (var scanner in scanners)
        {
            buffer.Position = 0;
            if (!await scanner.IsCleanAsync(buffer, name, contentType, cancellationToken))
            {
                return ChatErrors.Rejected;
            }
        }

        buffer.Position = 0;
        var key = BlobKeys.New(views.TenantId, clock);
        await storage.PutAsync(key, buffer, contentType, cancellationToken);
        var attachment = new ChatAttachment
        {
            TenantId = views.TenantId,
            ConversationId = command.ConversationId,
            UploadedBy = views.Me,
            Name = name,
            ContentType = contentType,
            Size = buffer.Length,
            BlobKey = key,
            ThumbnailKey = await ThumbnailAsync(buffer, key, contentType, cancellationToken),
            UploadedAt = clock.GetUtcNow(),
        };
        db.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);
        return ChatViews.ToDto(attachment);
    }

    // members only; the uploader also before the message is sent
    public async Task<Result<ChatAttachmentContent>> HandleAsync(OpenChatAttachmentQuery query, CancellationToken cancellationToken)
    {
        var me = views.Me;
        var attachment = await db.Set<ChatAttachment>().AsNoTracking()
            .Where(a => a.Id == query.AttachmentId && (a.MessageId != null || a.UploadedBy == me)
                        && db.Set<ChatMember>().Any(m => m.ConversationId == a.ConversationId && m.UserId == me))
            .SingleOrDefaultAsync(cancellationToken);
        var key = query.Thumbnail ? attachment?.ThumbnailKey : attachment?.BlobKey;
        if (key is null || await storage.OpenReadAsync(key, cancellationToken) is not { } content)
        {
            return ChatErrors.MessageNotFound;
        }

        return query.Thumbnail
            ? new ChatAttachmentContent(content, ChatThumbnails.ContentType, Path.ChangeExtension(attachment!.Name, ".webp"))
            : new ChatAttachmentContent(content, attachment!.ContentType, attachment.Name);
    }

    private async Task<string?> ThumbnailAsync(Stream buffer, string key, string contentType, CancellationToken cancellationToken)
    {
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        buffer.Position = 0;
        byte[]? thumbnail;
        try
        {
            thumbnail = ChatThumbnails.Create(buffer);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // a picture Skia cannot read is still sent, just without the small version
            return null;
        }

        if (thumbnail is null)
        {
            return null;
        }

        var thumbnailKey = ChatThumbnails.KeyOf(key);
        await storage.PutAsync(thumbnailKey, new MemoryStream(thumbnail), ChatThumbnails.ContentType, cancellationToken);
        return thumbnailKey;
    }

    public async Task<long> MaxBytesAsync(CancellationToken cancellationToken)
    {
        var megabytes = services.GetService<ISettingProvider>() is { } settings ? await settings.GetAsync<int>(ChatSettings.MaxAttachmentMb, cancellationToken) : 0;
        return (megabytes > 0 ? megabytes : ChatLimits.DefaultAttachmentMb) * 1024L * 1024L;
    }

    /// <returns>The bytes copied, -1 beyond the limit.</returns>
    private static async Task<long> CopyAsync(Stream source, Stream target, long limit, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit)
            {
                return -1;
            }

            await target.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return total;
    }

    private static string? CleanName(string? name)
    {
        var file = Path.GetFileName((name ?? string.Empty).Replace('\\', '/')).Trim();
        file = new string([.. file.Where(c => !char.IsControl(c) && c is not ('<' or '>' or ':' or '"' or '|' or '?' or '*'))]);
        return file.Length is 0 or > ChatLimits.NameLength || file is "." or ".." ? null : file;
    }
}
