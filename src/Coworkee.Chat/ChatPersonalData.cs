using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Chat;

/// <summary>
/// The messages a user sent and those of his direct chats; erasing removes them with their attachments, his reactions and memberships, and his direct chats with both sides.
/// </summary>
internal sealed class ChatPersonalData(CoworkeeDbContext db, IBlobStorage storage) : IPersonalDataContributor
{
    public string Section => "chat";

    /// <summary>What he sent anywhere and what he received in direct chats, his memberships, reactions and the files he uploaded (listed, not included).</summary>
    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var userId = subject.UserId;
        var direct = await DirectAsync(userId, cancellationToken);
        return new
        {
            Messages = await db.Set<ChatMessage>().IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.DeletedAt == null && (m.FromUserId == userId || direct.Contains(m.ConversationId)))
                .OrderBy(m => m.SentAt)
                .Select(m => new { m.ConversationId, m.FromUserId, m.Text, m.SentAt, m.EditedAt })
                .ToListAsync(cancellationToken),
            Memberships = await (from member in db.Set<ChatMember>().IgnoreQueryFilters().AsNoTracking()
                                 join conversation in db.Set<ChatConversation>().IgnoreQueryFilters() on member.ConversationId equals conversation.Id
                                 where member.UserId == userId
                                 orderby member.JoinedAt
                                 select new { member.ConversationId, conversation.IsGroup, conversation.Name, member.IsOwner, member.JoinedAt, member.LastReadAt })
                .ToListAsync(cancellationToken),
            Reactions = await db.Set<ChatReaction>().IgnoreQueryFilters().AsNoTracking().Where(r => r.UserId == userId)
                .Select(r => new { r.MessageId, r.Emoji }).ToListAsync(cancellationToken),
            Attachments = await db.Set<ChatAttachment>().IgnoreQueryFilters().AsNoTracking().Where(a => a.UploadedBy == userId).OrderBy(a => a.UploadedAt)
                .Select(a => new { a.ConversationId, a.MessageId, a.Name, a.ContentType, a.Size, a.UploadedAt }).ToListAsync(cancellationToken),
        };
    }

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var userId = subject.UserId;
        var direct = await DirectAsync(userId, cancellationToken);
        var messages = db.Set<ChatMessage>().IgnoreQueryFilters().Where(m => m.FromUserId == userId || m.ToUserId == userId || direct.Contains(m.ConversationId));
        var attachments = db.Set<ChatAttachment>().IgnoreQueryFilters()
            .Where(a => a.UploadedBy == userId || direct.Contains(a.ConversationId) || messages.Any(m => m.Id == a.MessageId));
        foreach (var blobs in await attachments.Select(a => new { a.BlobKey, a.ThumbnailKey }).ToListAsync(cancellationToken))
        {
            await ChatAttachmentBlobs.DeleteAsync(storage, blobs.BlobKey, blobs.ThumbnailKey, cancellationToken);
        }

        await attachments.ExecuteDeleteAsync(cancellationToken);
        await db.Set<ChatReaction>().IgnoreQueryFilters().Where(r => r.UserId == userId || messages.Any(m => m.Id == r.MessageId)).ExecuteDeleteAsync(cancellationToken);
        await messages.ExecuteDeleteAsync(cancellationToken);
        await db.Set<ChatMember>().IgnoreQueryFilters().Where(m => m.UserId == userId || direct.Contains(m.ConversationId)).ExecuteDeleteAsync(cancellationToken);
        await db.Set<ChatConversation>().IgnoreQueryFilters().Where(c => direct.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<List<Guid>> DirectAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<ChatConversation>().IgnoreQueryFilters()
            .Where(c => !c.IsGroup && db.Set<ChatMember>().IgnoreQueryFilters().Any(m => m.ConversationId == c.Id && m.UserId == userId))
            .Select(c => c.Id).ToListAsync(cancellationToken);
}
