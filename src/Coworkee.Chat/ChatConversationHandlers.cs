using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Chat;

internal sealed class ChatConversationHandlers(CoworkeeDbContext db, ChatViews views, ChatBroadcast broadcast, TimeProvider clock)
    : IHandler<GetChatsQuery, Result<IReadOnlyList<ChatConversationDto>>>,
      IHandler<GetChatQuery, Result<ChatConversationDto>>,
      IHandler<OpenDirectChatCommand, Result<ChatConversationDto>>,
      IHandler<CreateChatGroupCommand, Result<ChatConversationDto>>,
      IHandler<RenameChatCommand, Result<ChatConversationDto>>,
      IHandler<AddChatMembersCommand, Result<ChatConversationDto>>,
      IHandler<RemoveChatMemberCommand, Result>,
      IHandler<MarkChatReadByIdCommand, Result>,
      IHandler<ChatTypingCommand, Result>,
      IHandler<GetChatUnreadCountQuery, Result<Contracts.Notifications.UnreadCountDto>>
{
    public async Task<Result<IReadOnlyList<ChatConversationDto>>> HandleAsync(GetChatsQuery query, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<ChatConversationDto>>.Success(await views.ConversationsAsync(db.Set<ChatConversation>(), cancellationToken));

    public async Task<Result<ChatConversationDto>> HandleAsync(GetChatQuery query, CancellationToken cancellationToken) =>
        await views.FindAsync(query.ConversationId, cancellationToken) is null ? ChatErrors.NotFound : await views.ConversationAsync(query.ConversationId, cancellationToken);

    public async Task<Result<Contracts.Notifications.UnreadCountDto>> HandleAsync(GetChatUnreadCountQuery query, CancellationToken cancellationToken) =>
        new Contracts.Notifications.UnreadCountDto(await views.UnreadAsync(cancellationToken));

    public async Task<Result<ChatConversationDto>> HandleAsync(OpenDirectChatCommand command, CancellationToken cancellationToken)
    {
        var opened = await OpenDirectAsync(command.UserId, cancellationToken);
        return opened.IsSuccess ? await views.ConversationAsync(opened.Value.Id, cancellationToken) : opened.Error!;
    }

    /// <summary>The direct chat of the current user with a person, created on first use.</summary>
    public async Task<Result<ChatConversation>> OpenDirectAsync(Guid userId, CancellationToken cancellationToken)
    {
        var me = views.Me;
        if (userId == me || !await views.People().AnyAsync(u => u.Id == userId, cancellationToken))
        {
            return ChatErrors.UnknownPerson;
        }

        var key = ChatConversation.KeyOf(me, userId);
        if (await db.Set<ChatConversation>().SingleOrDefaultAsync(c => c.DirectKey == key, cancellationToken) is { } existing)
        {
            return existing;
        }

        var now = clock.GetUtcNow();
        var conversation = new ChatConversation { TenantId = views.TenantId, DirectKey = key, CreatedBy = me, CreatedAt = now };
        db.Add(conversation);
        db.AddRange(Member(conversation, me, now, false), Member(conversation, userId, now, false));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // the other person opened it at the same moment
            db.ChangeTracker.Clear();
            return await db.Set<ChatConversation>().SingleAsync(c => c.DirectKey == key, cancellationToken);
        }

        return conversation;
    }

    public async Task<Result<ChatConversationDto>> HandleAsync(CreateChatGroupCommand command, CancellationToken cancellationToken)
    {
        var me = views.Me;
        var people = await KnownAsync(command.MemberIds, cancellationToken);
        if (people.Count != command.MemberIds.Distinct().Count(id => id != me))
        {
            return ChatErrors.UnknownPerson;
        }

        var now = clock.GetUtcNow();
        var conversation = new ChatConversation { TenantId = views.TenantId, IsGroup = true, Name = command.Name.Trim(), CreatedBy = me, CreatedAt = now };
        db.Add(conversation);
        db.Add(Member(conversation, me, now, true));
        db.AddRange(people.Select(id => Member(conversation, id, now, false)));
        await db.SaveChangesAsync(cancellationToken);
        return await PublishAsync(conversation.Id, [], cancellationToken);
    }

    public async Task<Result<ChatConversationDto>> HandleAsync(RenameChatCommand command, CancellationToken cancellationToken)
    {
        if (await views.FindAsync(command.ConversationId, cancellationToken) is not { } conversation)
        {
            return ChatErrors.NotFound;
        }

        if (!conversation.IsGroup)
        {
            return ChatErrors.NotAGroup;
        }

        conversation.Name = command.Name.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return await PublishAsync(conversation.Id, [], cancellationToken);
    }

    public async Task<Result<ChatConversationDto>> HandleAsync(AddChatMembersCommand command, CancellationToken cancellationToken)
    {
        if (await views.FindAsync(command.ConversationId, cancellationToken) is not { } conversation)
        {
            return ChatErrors.NotFound;
        }

        if (!conversation.IsGroup)
        {
            return ChatErrors.NotAGroup;
        }

        var people = await KnownAsync(command.UserIds, cancellationToken);
        if (people.Count != command.UserIds.Distinct().Count(id => id != views.Me))
        {
            return ChatErrors.UnknownPerson;
        }

        var present = await views.MemberIdsAsync(conversation.Id, cancellationToken);
        var now = clock.GetUtcNow();
        db.AddRange(people.Except(present).Select(id => Member(conversation, id, now, false)));
        await db.SaveChangesAsync(cancellationToken);
        return await PublishAsync(conversation.Id, [], cancellationToken);
    }

    public async Task<Result> HandleAsync(RemoveChatMemberCommand command, CancellationToken cancellationToken)
    {
        if (await views.FindAsync(command.ConversationId, cancellationToken) is not { } conversation)
        {
            return ChatErrors.NotFound;
        }

        if (!conversation.IsGroup)
        {
            return ChatErrors.NotAGroup;
        }

        var members = await db.Set<ChatMember>().Where(m => m.ConversationId == conversation.Id).ToListAsync(cancellationToken);
        if (command.UserId != views.Me && !members.Any(m => m.UserId == views.Me && m.IsOwner))
        {
            return ChatErrors.NotOwner;
        }

        if (members.SingleOrDefault(m => m.UserId == command.UserId) is not { } leaving)
        {
            return Result.Success();
        }

        db.Remove(leaving);
        await db.SaveChangesAsync(cancellationToken);
        if (members.Count > 1)
        {
            await PublishAsync(conversation.Id, [command.UserId], cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> HandleAsync(MarkChatReadByIdCommand command, CancellationToken cancellationToken) =>
        await MarkReadAsync(command.ConversationId, cancellationToken) ? Result.Success() : ChatErrors.NotFound;

    /// <summary>Moves the read mark of the current user to now when someone else wrote since; false when he is not in the chat.</summary>
    public async Task<bool> MarkReadAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var me = views.Me;
        if (await db.Set<ChatMember>().SingleOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == me, cancellationToken) is not { } member)
        {
            return false;
        }

        var unread = await db.Set<ChatMessage>().AnyAsync(
            m => m.ConversationId == conversationId && m.FromUserId != me && (member.LastReadAt == null || m.SentAt > member.LastReadAt), cancellationToken);
        if (unread)
        {
            var now = clock.GetUtcNow();
            member.LastReadAt = now;
            await db.SaveChangesAsync(cancellationToken);
            var members = await views.MemberIdsAsync(conversationId, cancellationToken);
            var conversation = await db.Set<ChatConversation>().AsNoTracking().SingleAsync(c => c.Id == conversationId, cancellationToken);
            var sender = conversation.IsGroup ? Guid.Empty : members.FirstOrDefault(id => id != me);
            await broadcast.SendAsync(members, ChatEvents.Read, new ChatReadDto(me, sender, now) { ConversationId = conversationId }, cancellationToken);
        }

        return true;
    }

    public async Task<Result> HandleAsync(ChatTypingCommand command, CancellationToken cancellationToken)
    {
        var members = await views.MemberIdsAsync(command.ConversationId, cancellationToken);
        if (!members.Contains(views.Me))
        {
            return ChatErrors.NotFound;
        }

        await broadcast.SendAsync(members.Where(id => id != views.Me), ChatEvents.Typing, new ChatTypingDto(command.ConversationId, views.Me), cancellationToken);
        return Result.Success();
    }

    private async Task<List<Guid>> KnownAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Where(id => id != views.Me).Distinct().ToList();
        return await views.People().Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken);
    }

    private async Task<ChatConversationDto> PublishAsync(Guid conversationId, IReadOnlyList<Guid> alsoTo, CancellationToken cancellationToken)
    {
        var dto = await views.ConversationAsync(conversationId, cancellationToken, onlyMine: false);
        await broadcast.SendAsync(dto.Members.Select(m => m.UserId).Concat(alsoTo), ChatEvents.Conversation, dto, cancellationToken);
        return dto;
    }

    private static ChatMember Member(ChatConversation conversation, Guid userId, DateTimeOffset now, bool owner) =>
        new() { TenantId = conversation.TenantId, ConversationId = conversation.Id, UserId = userId, JoinedAt = now, LastReadAt = now, IsOwner = owner };
}
