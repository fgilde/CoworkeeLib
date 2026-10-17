using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Notifications;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Chat;

internal sealed class ChatMessageHandlers(
    CoworkeeDbContext db, ChatViews views, ChatBroadcast broadcast, ChatEntityAccess entities, IBlobStorage storage, IServiceProvider services, TimeProvider clock)
    : IHandler<GetChatMessagesQuery, Result<IReadOnlyList<ChatMessageDto>>>,
      IHandler<GetChatMessageQuery, Result<ChatMessageDto>>,
      IHandler<PostChatMessageCommand, Result<ChatMessageDto>>,
      IHandler<EditChatMessageCommand, Result<ChatMessageDto>>,
      IHandler<DeleteChatMessageCommand, Result>,
      IHandler<ToggleChatReactionCommand, Result<ChatMessageDto>>
{
    private static readonly Error UnknownRecord = Error.Validation("chat.unknown_record", "A mentioned record does not exist or is not yours to see.");
    private static readonly Error UnknownReply = Error.Validation("chat.unknown_reply", "The answered message is not in this chat.");

    public async Task<Result<IReadOnlyList<ChatMessageDto>>> HandleAsync(GetChatMessagesQuery query, CancellationToken cancellationToken)
    {
        if (await views.FindAsync(query.ConversationId, cancellationToken) is null)
        {
            return ChatErrors.NotFound;
        }

        var messages = db.Set<ChatMessage>().AsNoTracking().Where(m => m.ConversationId == query.ConversationId && (query.Before == null || m.SentAt < query.Before));
        foreach (var word in (query.Search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var lower = word.ToLower();
            messages = messages.Where(m => m.DeletedAt == null && m.Text.ToLower().Contains(lower));
        }

        var page = await messages.OrderByDescending(m => m.SentAt).Take(query.Take).ToListAsync(cancellationToken);
        page.Reverse();
        return await views.MessagesAsync(page, true, cancellationToken);
    }

    public async Task<Result<ChatMessageDto>> HandleAsync(GetChatMessageQuery query, CancellationToken cancellationToken) =>
        await FindAsync(query.MessageId, cancellationToken) is { } message ? await views.MessageAsync(message, true, cancellationToken) : ChatErrors.MessageNotFound;

    public async Task<Result<ChatMessageDto>> HandleAsync(PostChatMessageCommand command, CancellationToken cancellationToken)
    {
        if (await views.FindAsync(command.ConversationId, cancellationToken) is not { } conversation)
        {
            return ChatErrors.NotFound;
        }

        return await PostAsync(conversation, ChatHtml.Parse(command.Html), command.ReplyToId, command.AttachmentIds ?? [], cancellationToken);
    }

    /// <summary>Sends parsed content to a chat the current user is in.</summary>
    public async Task<Result<ChatMessageDto>> PostAsync(
        ChatConversation conversation, ChatContent content, Guid? replyToId, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken)
    {
        var me = views.Me;
        var ids = attachmentIds.Distinct().ToList();
        var attachments = ids.Count == 0 ? [] : await db.Set<ChatAttachment>()
            .Where(a => ids.Contains(a.Id) && a.ConversationId == conversation.Id && a.UploadedBy == me && a.MessageId == null).ToListAsync(cancellationToken);
        if (attachments.Count != ids.Count)
        {
            return ChatErrors.NotFound;
        }

        if (content.Text.Length == 0 && attachments.Count == 0)
        {
            return ChatErrors.Empty;
        }

        if (replyToId is { } reply && !await db.Set<ChatMessage>().AnyAsync(m => m.Id == reply && m.ConversationId == conversation.Id, cancellationToken))
        {
            return UnknownReply;
        }

        if (!await MayMentionAsync(content, cancellationToken))
        {
            return UnknownRecord;
        }

        var now = clock.GetUtcNow();
        var message = new ChatMessage
        {
            TenantId = conversation.TenantId,
            ConversationId = conversation.Id,
            FromUserId = me,
            Html = content.Html,
            Text = TextOf(content, attachments),
            ReplyToId = replyToId,
            SentAt = now,
        };
        db.Add(message);
        attachments.ForEach(a => a.MessageId = message.Id);
        conversation.LastMessageAt = now;
        await db.Set<ChatMember>().Where(m => m.ConversationId == conversation.Id && m.UserId == me)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.LastReadAt, now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(message, cancellationToken);
        await NotifyMentionedAsync(message, content, cancellationToken);
        return await views.MessageAsync(message, true, cancellationToken);
    }

    public async Task<Result<ChatMessageDto>> HandleAsync(EditChatMessageCommand command, CancellationToken cancellationToken)
    {
        if (await OwnAsync(command.MessageId, cancellationToken) is not { } message)
        {
            return await FindAsync(command.MessageId, cancellationToken) is null ? ChatErrors.MessageNotFound : ChatErrors.NotYours;
        }

        var content = ChatHtml.Parse(command.Html);
        var attachments = await db.Set<ChatAttachment>().Where(a => a.MessageId == message.Id).ToListAsync(cancellationToken);
        if (content.Text.Length == 0 && attachments.Count == 0)
        {
            return ChatErrors.Empty;
        }

        if (!await MayMentionAsync(content, cancellationToken))
        {
            return UnknownRecord;
        }

        message.Html = content.Html;
        message.Text = TextOf(content, attachments);
        message.EditedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(message, cancellationToken);
        return await views.MessageAsync(message, true, cancellationToken);
    }

    public async Task<Result> HandleAsync(DeleteChatMessageCommand command, CancellationToken cancellationToken)
    {
        if (await OwnAsync(command.MessageId, cancellationToken) is not { } message)
        {
            return await FindAsync(command.MessageId, cancellationToken) is null ? ChatErrors.MessageNotFound : ChatErrors.NotYours;
        }

        var attachments = await db.Set<ChatAttachment>().Where(a => a.MessageId == message.Id).ToListAsync(cancellationToken);
        message.DeletedAt = clock.GetUtcNow();
        message.Html = null;
        message.Text = string.Empty;
        db.RemoveRange(attachments);
        await db.Set<ChatReaction>().Where(r => r.MessageId == message.Id).ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var attachment in attachments)
        {
            await storage.DeleteAsync(attachment.BlobKey, cancellationToken);
        }

        await PublishAsync(message, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ChatMessageDto>> HandleAsync(ToggleChatReactionCommand command, CancellationToken cancellationToken)
    {
        if (await FindAsync(command.MessageId, cancellationToken) is not { DeletedAt: null } message)
        {
            return ChatErrors.MessageNotFound;
        }

        var me = views.Me;
        if (await db.Set<ChatReaction>().SingleOrDefaultAsync(r => r.MessageId == message.Id && r.UserId == me && r.Emoji == command.Emoji, cancellationToken) is { } existing)
        {
            db.Remove(existing);
        }
        else
        {
            db.Add(new ChatReaction { TenantId = message.TenantId, MessageId = message.Id, UserId = me, Emoji = command.Emoji });
        }

        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(message, cancellationToken);
        return await views.MessageAsync(message, true, cancellationToken);
    }

    /// <summary>A message of a chat the current user is in, tracked.</summary>
    private Task<ChatMessage?> FindAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var me = views.Me;
        return db.Set<ChatMessage>()
            .Where(m => m.Id == messageId && db.Set<ChatMember>().Any(x => x.ConversationId == m.ConversationId && x.UserId == me))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<ChatMessage?> OwnAsync(Guid messageId, CancellationToken cancellationToken) =>
        await FindAsync(messageId, cancellationToken) is { DeletedAt: null } message && message.FromUserId == views.Me ? message : null;

    // the sender must see every record he mentions; otherwise a forged mention would carry a label he made up
    private async Task<bool> MayMentionAsync(ChatContent content, CancellationToken cancellationToken)
    {
        var records = content.Mentions.Where(m => m.Kind == ChatMentionItem.Entity).ToList();
        if (records.Count == 0)
        {
            return true;
        }

        var visible = await entities.CheckAsync(records, cancellationToken);
        return records.All(visible);
    }

    private static string TextOf(ChatContent content, IReadOnlyList<ChatAttachment> attachments) =>
        content.Text.Length > 0 || attachments.Count == 0 ? content.Text : "📎 " + string.Join(", ", attachments.Select(a => a.Name));

    private async Task PublishAsync(ChatMessage message, CancellationToken cancellationToken) =>
        await broadcast.ToMembersAsync(message.ConversationId, ChatEvents.Message, await views.MessageAsync(message, false, cancellationToken) with { Source = null }, cancellationToken);

    // only members hear about a mention: someone outside the chat could not read it anyway
    private async Task NotifyMentionedAsync(ChatMessage message, ChatContent content, CancellationToken cancellationToken)
    {
        if (services.GetService<INotifier>() is not { } notifier)
        {
            return;
        }

        var members = await views.MemberIdsAsync(message.ConversationId, cancellationToken);
        var mentioned = content.Mentions.Where(m => m.Kind == ChatMentionItem.User).Select(m => Guid.Parse(m.Id))
            .Where(id => id != message.FromUserId && members.Contains(id)).Distinct().ToList();
        if (mentioned.Count == 0)
        {
            return;
        }

        var sender = (await views.NamesAsync([message.FromUserId], cancellationToken)).GetValueOrDefault(message.FromUserId, string.Empty);
        await notifier.NotifyLocalizedAsync(mentioned, ChatNotificationTypes.Mention, "{0} mentioned you in the chat", "{1}", [sender, message.Text],
            $"/chat?c={message.ConversationId}", cancellationToken);
    }
}
