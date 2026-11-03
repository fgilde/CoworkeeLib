using System.Net.Http.Headers;
using Coworkee.Contracts.Attachments;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files.Attachments;

internal sealed class RecordAttachments(
    CoworkeeDbContext db, RecordAttachmentOptions options, IBlobStorage storage, AttachmentBlobs blobs, ICurrentUser currentUser, TimeProvider clock,
    IEnumerable<IAttachmentScanner> scanners) : IRecordAttachments
{
    private const string FallbackContentType = "application/octet-stream";

    public static readonly Error UnknownSlot = Error.NotFound("attachments.slot_not_found", "The record type has no such attachment slot.");

    public async Task<Result<RecordAttachmentDto>> PutAsync(string entityType, Guid entityId, string slot, string fileName, string? contentType, Stream content, CancellationToken cancellationToken)
    {
        if (options.Find(entityType)?.Slots.GetValueOrDefault(slot) is not { } definition)
        {
            return UnknownSlot;
        }

        if (FileNames.Clean(fileName) is not { } name)
        {
            return Error.Validation("fileName", "The file name is empty, too long or contains path characters.");
        }

        var type = MediaTypeHeaderValue.TryParse(contentType, out var parsed) && parsed.MediaType is { Length: <= 200 } mediaType ? mediaType : FallbackContentType;
        if (!definition.Accepts(type))
        {
            return Error.Validation("contentType", $"The slot takes {string.Join(", ", definition.ContentTypes)}, not {type}.");
        }

        if (currentUser.TenantId is not { } tenantId)
        {
            return FileErrors.NoTenant;
        }

        // ponytail: buffered to a temp file so every provider gets a seekable stream of known length and scanners can read it first
        await using var buffer = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        if (!await CopyAsync(content, buffer, definition.MaxBytes, cancellationToken))
        {
            return Error.Validation("content", $"The file is larger than {definition.MaxBytes / 1024 / 1024.0:0.#} MB.");
        }

        if (buffer.Length == 0)
        {
            return Error.Validation("content", "The file is empty.");
        }

        var upload = new AttachmentUpload(entityType, entityId, slot, name, type, buffer.Length);
        foreach (var scanner in scanners)
        {
            buffer.Position = 0;
            if (await scanner.ScanAsync(upload, buffer, cancellationToken) is { } reason)
            {
                return Error.Validation("content", reason);
            }
        }

        buffer.Position = 0;
        var key = BlobKeys.New(tenantId, clock);
        blobs.Written(db, key);
        await storage.PutAsync(key, buffer, type, cancellationToken);
        var replaced = definition.Multiple ? [] : await Of(entityType, entityId, slot).ToListAsync(cancellationToken);
        if (!definition.Multiple)
        {
            replaced.AddRange(db.ChangeTracker.Entries<RecordAttachment>()
                .Where(e => e.State == EntityState.Added && e.Entity.EntityType == entityType && e.Entity.EntityId == entityId && e.Entity.Slot == slot).Select(e => e.Entity));
        }

        var attachment = new RecordAttachment
        {
            TenantId = tenantId, EntityType = entityType, EntityId = entityId, Slot = slot, FileName = name, ContentType = type, Size = buffer.Length, BlobKey = key,
            CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        Remove(replaced);
        db.Add(attachment);
        return ToDto(attachment);
    }

    public async Task<RecordAttachmentFile?> OpenAsync(Guid attachmentId, CancellationToken cancellationToken) =>
        await db.Set<RecordAttachment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == attachmentId, cancellationToken) is { } attachment
        && await storage.OpenReadAsync(attachment.BlobKey, cancellationToken) is { } content
            ? new RecordAttachmentFile(ToDto(attachment), content)
            : null;

    public async Task<bool> DeleteAsync(Guid attachmentId, CancellationToken cancellationToken) =>
        Remove(await db.Set<RecordAttachment>().Where(a => a.Id == attachmentId).ToListAsync(cancellationToken)) > 0;

    public async Task DeleteAllAsync(string entityType, Guid entityId, CancellationToken cancellationToken) =>
        Remove(await db.Set<RecordAttachment>().Where(a => a.EntityType == entityType && a.EntityId == entityId).ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<RecordAttachmentDto>> ListAsync(string entityType, Guid entityId, string? slot = null, CancellationToken cancellationToken = default) =>
        await db.Set<RecordAttachment>().AsNoTracking().Where(a => a.EntityType == entityType && a.EntityId == entityId && (slot == null || a.Slot == slot))
            .OrderBy(a => a.CreatedAt).Select(a => ToDto(a)).ToListAsync(cancellationToken);

    internal static RecordAttachmentDto ToDto(RecordAttachment a) => new(a.Id, a.EntityType, a.EntityId, a.Slot, a.FileName, a.ContentType, a.Size, a.CreatedAt, a.CreatedBy);

    private int Remove(List<RecordAttachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            blobs.Removed(db, attachment.BlobKey);
        }

        db.RemoveRange(attachments);
        return attachments.Count;
    }

    private IQueryable<RecordAttachment> Of(string entityType, Guid entityId, string slot) =>
        db.Set<RecordAttachment>().Where(a => a.EntityType == entityType && a.EntityId == entityId && a.Slot == slot);

    private static async Task<bool> CopyAsync(Stream source, Stream target, long maxBytes, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (target.Length + read > maxBytes)
            {
                return false;
            }

            await target.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return true;
    }
}
