using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Chat;

internal static class ChatErrors
{
    public static readonly Error UnknownPerson = Error.NotFound("chat.unknown_person", "There is no such person in the organisation.");
    public static readonly Error NotFound = Error.NotFound("chat.not_found", "There is no such chat.");
    public static readonly Error MessageNotFound = Error.NotFound("chat.message_not_found", "There is no such message.");
    public static readonly Error NotYours = Error.Forbidden("chat.not_yours", "Only the sender changes or deletes a message.");
    public static readonly Error NotOwner = Error.Forbidden("chat.not_owner", "Only the owner of the group removes others.");
    public static readonly Error NotAGroup = Error.Validation("chat.not_a_group", "A direct chat has no name and keeps its two people.");
    public static readonly Error Empty = Error.Validation("chat.empty", "Write something or attach a file.");
    public static readonly Error TooLarge = Error.Validation("chat.too_large", "The file is larger than allowed.");
    public static readonly Error Rejected = Error.Validation("chat.rejected", "The file was rejected by the virus scan.");
    public static readonly Error InvalidName = Error.Validation("chat.invalid_name", "The file name is not valid.");
}

/// <summary>Who is who in the chats of the current user, and how conversations and messages look to him.</summary>
internal sealed class ChatViews(CoworkeeDbContext db, ICurrentUser currentUser, ChatEntityAccess entities)
{
    public Guid Me => currentUser.UserId ?? throw new InvalidOperationException("Chat needs a signed-in user.");

    public Guid TenantId => currentUser.TenantId ?? throw new InvalidOperationException("Chat needs an organisation.");

    public IQueryable<User> People() => db.Set<User>().Where(u => u.TenantId == currentUser.TenantId && u.IsActive);

    public IQueryable<ChatMember> Memberships() => db.Set<ChatMember>().Where(m => m.UserId == Me);

    /// <summary>The conversation when the current user is in it, tracked.</summary>
    public Task<ChatConversation?> FindAsync(Guid conversationId, CancellationToken cancellationToken) =>
        db.Set<ChatConversation>().Where(c => c.Id == conversationId && db.Set<ChatMember>().Any(m => m.ConversationId == c.Id && m.UserId == Me))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<List<Guid>> MemberIdsAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await db.Set<ChatMember>().Where(m => m.ConversationId == conversationId).Select(m => m.UserId).ToListAsync(cancellationToken);

    public async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        return (await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id) && u.TenantId == currentUser.TenantId)
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).ToListAsync(cancellationToken))
            .ToDictionary(u => u.Id, u => Name(u.FirstName, u.LastName, u.Email));
    }

    public static string Name(string? firstName, string? lastName, string? email) =>
        string.Join(' ', new[] { firstName, lastName }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } name ? name : email ?? string.Empty;

    public async Task<ChatConversationDto> ConversationAsync(Guid conversationId, CancellationToken cancellationToken, bool onlyMine = true) =>
        (await ConversationsAsync(db.Set<ChatConversation>().Where(c => c.Id == conversationId), cancellationToken, onlyMine)).Single();

    /// <summary>The conversations among <paramref name="which"/> the current user is in (any with <paramref name="onlyMine"/> false), newest first.</summary>
    public async Task<IReadOnlyList<ChatConversationDto>> ConversationsAsync(IQueryable<ChatConversation> which, CancellationToken cancellationToken, bool onlyMine = true)
    {
        var me = Me;
        var conversations = await which.AsNoTracking().Where(c => !onlyMine || db.Set<ChatMember>().Any(m => m.ConversationId == c.Id && m.UserId == me)).ToListAsync(cancellationToken);
        var ids = conversations.Select(c => c.Id).ToList();
        var members = await db.Set<ChatMember>().AsNoTracking().Where(m => ids.Contains(m.ConversationId)).ToListAsync(cancellationToken);
        var names = await NamesAsync(members.Select(m => m.UserId), cancellationToken);
        var mine = members.Where(m => m.UserId == me).ToDictionary(m => m.ConversationId);
        var last = await db.Set<ChatMessage>().AsNoTracking().Where(m => ids.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => g.OrderByDescending(m => m.SentAt).Select(m => new { m.ConversationId, m.Text, m.SentAt, m.DeletedAt }).First())
            .ToDictionaryAsync(m => m.ConversationId, cancellationToken);
        var unread = await (from message in db.Set<ChatMessage>()
                            join member in db.Set<ChatMember>() on message.ConversationId equals member.ConversationId
                            where member.UserId == me && ids.Contains(message.ConversationId) && message.FromUserId != me && message.DeletedAt == null
                                  && (member.LastReadAt == null || message.SentAt > member.LastReadAt)
                            group message by message.ConversationId into g
                            select new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return conversations
            .Select(c =>
            {
                var people = members.Where(m => m.ConversationId == c.Id)
                    .Select(m => new ChatMemberDto(m.UserId, names.GetValueOrDefault(m.UserId, string.Empty), m.LastReadAt, m.IsOwner))
                    .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                var name = c.IsGroup ? c.Name ?? string.Empty : people.FirstOrDefault(p => p.UserId != me)?.Name ?? string.Empty;
                var lastMessage = last.GetValueOrDefault(c.Id);
                return new ChatConversationDto(c.Id, c.IsGroup, name, people, lastMessage?.DeletedAt is null ? lastMessage?.Text : null, lastMessage?.SentAt,
                    unread.GetValueOrDefault(c.Id));
            })
            .OrderByDescending(c => c.LastAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    public async Task<int> UnreadAsync(CancellationToken cancellationToken)
    {
        var me = Me;
        return await (from message in db.Set<ChatMessage>()
                      join member in db.Set<ChatMember>() on message.ConversationId equals member.ConversationId
                      where member.UserId == me && message.FromUserId != me && message.DeletedAt == null
                            && (member.LastReadAt == null || message.SentAt > member.LastReadAt)
                      select message.Id).CountAsync(cancellationToken);
    }

    /// <param name="resolveEntities">False for realtime events: records show as restricted until the reader loads the message.</param>
    public async Task<List<ChatMessageDto>> MessagesAsync(IReadOnlyList<ChatMessage> messages, bool resolveEntities, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return [];
        }

        var me = Me;
        var ids = messages.Select(m => m.Id).ToList();
        var conversationIds = messages.Select(m => m.ConversationId).Distinct().ToList();
        var conversations = await db.Set<ChatConversation>().AsNoTracking().Where(c => conversationIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var members = await db.Set<ChatMember>().AsNoTracking().Where(m => conversationIds.Contains(m.ConversationId)).ToListAsync(cancellationToken);
        var reactions = await db.Set<ChatReaction>().AsNoTracking().Where(r => ids.Contains(r.MessageId)).ToListAsync(cancellationToken);
        var attachments = await db.Set<ChatAttachment>().AsNoTracking().Where(a => a.MessageId != null && ids.Contains(a.MessageId.Value)).ToListAsync(cancellationToken);
        var replyIds = messages.Where(m => m.ReplyToId is not null).Select(m => m.ReplyToId!.Value).Distinct().ToList();
        var replies = await db.Set<ChatMessage>().AsNoTracking().Where(m => replyIds.Contains(m.Id))
            .Select(m => new { m.Id, m.FromUserId, m.Text, m.DeletedAt }).ToDictionaryAsync(m => m.Id, cancellationToken);
        var mentions = messages.Where(m => m.DeletedAt is null).ToDictionary(m => m.Id, m => ChatHtml.Mentions(m.Html));
        var names = await NamesAsync(mentions.Values.SelectMany(l => l).Where(m => m.Kind == ChatMentionItem.User).Select(m => Guid.Parse(m.Id)), cancellationToken);
        var visible = resolveEntities ? await entities.CheckAsync(mentions.Values.SelectMany(l => l), cancellationToken) : null;

        return
        [
            .. messages.Select(m =>
            {
                var conversation = conversations.GetValueOrDefault(m.ConversationId);
                var other = conversation is { IsGroup: false } ? members.FirstOrDefault(x => x.ConversationId == m.ConversationId && x.UserId != m.FromUserId) : null;
                var dto = new ChatMessageDto(m.Id, m.FromUserId, other?.UserId,
                    m.DeletedAt is null ? m.Text : string.Empty, m.SentAt, other?.LastReadAt >= m.SentAt ? other.LastReadAt : null)
                {
                    ConversationId = m.ConversationId,
                    EditedAt = m.EditedAt,
                    Deleted = m.DeletedAt is not null,
                };
                if (m.DeletedAt is not null)
                {
                    return dto;
                }

                var own = mentions[m.Id];
                return dto with
                {
                    Html = ChatHtml.Render(m.Html, m.Text, id => names.GetValueOrDefault(id), visible),
                    Source = m.FromUserId == me ? m.Html ?? ChatHtml.FromText(m.Text) : null,
                    HasEntityMentions = own.Any(x => x.Kind == ChatMentionItem.Entity),
                    ReplyTo = m.ReplyToId is { } replyId && replies.GetValueOrDefault(replyId) is { } reply
                        ? new ChatReplyDto(reply.Id, reply.FromUserId, reply.DeletedAt is null ? reply.Text : string.Empty)
                        : null,
                    Reactions = [.. reactions.Where(r => r.MessageId == m.Id).GroupBy(r => r.Emoji).Select(g => new ChatReactionDto(g.Key, [.. g.Select(r => r.UserId)]))],
                    Attachments = [.. attachments.Where(a => a.MessageId == m.Id).OrderBy(a => a.UploadedAt).Select(ToDto)],
                };
            }),
        ];
    }

    public async Task<ChatMessageDto> MessageAsync(ChatMessage message, bool resolveEntities, CancellationToken cancellationToken) =>
        (await MessagesAsync([message], resolveEntities, cancellationToken)).Single();

    public static ChatAttachmentDto ToDto(ChatAttachment attachment) =>
        new(attachment.Id, attachment.Name, attachment.ContentType, attachment.Size) { HasThumbnail = attachment.ThumbnailKey is not null };
}
