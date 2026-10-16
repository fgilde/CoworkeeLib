using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Chat;

/// <summary>The direct chat API by person (the first chat API, still used by the AirWorks chat app and the assistant), on top of the conversations.</summary>
internal sealed class ChatHandlers(CoworkeeDbContext db, ChatViews views, ChatConversationHandlers conversations, ChatMessageHandlers messages)
    : IHandler<GetChatContactsQuery, Result<IReadOnlyList<ChatContactDto>>>,
      IHandler<GetChatConversationQuery, Result<IReadOnlyList<ChatMessageDto>>>,
      IHandler<SendChatMessageCommand, Result<ChatMessageDto>>,
      IHandler<MarkChatReadCommand, Result>
{
    public async Task<Result<IReadOnlyList<ChatContactDto>>> HandleAsync(GetChatContactsQuery query, CancellationToken cancellationToken)
    {
        var me = views.Me;
        var people = await views.People().Where(u => u.Id != me).Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).ToListAsync(cancellationToken);
        var direct = (await views.ConversationsAsync(db.Set<ChatConversation>().Where(c => !c.IsGroup), cancellationToken))
            .ToDictionary(c => c.Members.FirstOrDefault(m => m.UserId != me)?.UserId ?? Guid.Empty);
        return people
            .Select(p => (Person: p, Chat: direct.GetValueOrDefault(p.Id)))
            .Select(x => new ChatContactDto(x.Person.Id, ChatViews.Name(x.Person.FirstName, x.Person.LastName, x.Person.Email), x.Chat?.LastMessage, x.Chat?.LastAt, x.Chat?.Unread ?? 0))
            .OrderByDescending(c => c.LastAt)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<Result<IReadOnlyList<ChatMessageDto>>> HandleAsync(GetChatConversationQuery query, CancellationToken cancellationToken)
    {
        var key = ChatConversation.KeyOf(views.Me, query.UserId);
        var conversation = await db.Set<ChatConversation>().AsNoTracking().SingleOrDefaultAsync(c => c.DirectKey == key, cancellationToken);
        return conversation is null
            ? Result<IReadOnlyList<ChatMessageDto>>.Success([])
            : await messages.HandleAsync(new GetChatMessagesQuery(conversation.Id, query.Before, null, query.Take), cancellationToken);
    }

    public async Task<Result<ChatMessageDto>> HandleAsync(SendChatMessageCommand command, CancellationToken cancellationToken)
    {
        var conversation = await conversations.OpenDirectAsync(command.ToUserId, cancellationToken);
        return conversation.IsSuccess
            ? await messages.PostAsync(conversation.Value, ChatHtml.Parse(ChatHtml.FromText(command.Text)), null, [], cancellationToken)
            : conversation.Error!;
    }

    public async Task<Result> HandleAsync(MarkChatReadCommand command, CancellationToken cancellationToken)
    {
        var key = ChatConversation.KeyOf(views.Me, command.UserId);
        if (await db.Set<ChatConversation>().AsNoTracking().SingleOrDefaultAsync(c => c.DirectKey == key, cancellationToken) is { } conversation)
        {
            await conversations.MarkReadAsync(conversation.Id, cancellationToken);
        }

        return Result.Success();
    }
}
