using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Chat;

internal sealed class ChatHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IRealtimePublisher realtime, TimeProvider clock)
    : IHandler<GetChatContactsQuery, Result<IReadOnlyList<ChatContactDto>>>,
      IHandler<GetChatConversationQuery, Result<IReadOnlyList<ChatMessageDto>>>,
      IHandler<SendChatMessageCommand, Result<ChatMessageDto>>,
      IHandler<MarkChatReadCommand, Result>
{
    private static readonly Error UnknownPerson = Error.NotFound("chat.unknown_person", "There is no such person in the organisation.");

    private Guid Me => currentUser.UserId ?? throw new InvalidOperationException("Chat needs a signed-in user.");

    private IQueryable<ChatMessage> Mine => db.Set<ChatMessage>().Where(m => m.FromUserId == Me || m.ToUserId == Me);

    public async Task<Result<IReadOnlyList<ChatContactDto>>> HandleAsync(GetChatContactsQuery query, CancellationToken cancellationToken)
    {
        var me = Me;
        var people = await People().Where(u => u.Id != me).Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).ToListAsync(cancellationToken);
        var last = await Mine
            .GroupBy(m => m.FromUserId == me ? m.ToUserId : m.FromUserId)
            .Select(g => new
            {
                Contact = g.Key,
                Last = g.OrderByDescending(m => m.SentAt).Select(m => new { m.Text, m.SentAt }).First(),
                Unread = g.Count(m => m.ToUserId == me && m.ReadAt == null),
            })
            .ToDictionaryAsync(c => c.Contact, cancellationToken);
        return people
            .Select(p => last.TryGetValue(p.Id, out var chat)
                ? new ChatContactDto(p.Id, Name(p.FirstName, p.LastName, p.Email), chat.Last.Text, chat.Last.SentAt, chat.Unread)
                : new ChatContactDto(p.Id, Name(p.FirstName, p.LastName, p.Email), null, null, 0))
            .OrderByDescending(c => c.LastAt)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<Result<IReadOnlyList<ChatMessageDto>>> HandleAsync(GetChatConversationQuery query, CancellationToken cancellationToken)
    {
        var messages = await Between(query.UserId)
            .Where(m => query.Before == null || m.SentAt < query.Before)
            .OrderByDescending(m => m.SentAt)
            .Take(query.Take)
            .ToListAsync(cancellationToken);
        return messages.AsEnumerable().Reverse().Select(ToDto).ToList();
    }

    public async Task<Result<ChatMessageDto>> HandleAsync(SendChatMessageCommand command, CancellationToken cancellationToken)
    {
        if (command.ToUserId == Me || !await People().AnyAsync(u => u.Id == command.ToUserId, cancellationToken))
        {
            return UnknownPerson;
        }

        var message = new ChatMessage
        {
            TenantId = currentUser.TenantId!.Value,
            FromUserId = Me,
            ToUserId = command.ToUserId,
            Text = command.Text.Trim(),
            SentAt = clock.GetUtcNow(),
        };
        db.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        var dto = ToDto(message);
        await realtime.PublishAsync(message.TenantId, $"user:{message.ToUserId}", ChatEvents.Message, dto, cancellationToken);
        await realtime.PublishAsync(message.TenantId, $"user:{message.FromUserId}", ChatEvents.Message, dto, cancellationToken);
        return dto;
    }

    public async Task<Result> HandleAsync(MarkChatReadCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await db.Set<ChatMessage>()
            .Where(m => m.FromUserId == command.UserId && m.ToUserId == Me && m.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.ReadAt, now), cancellationToken);
        return Result.Success();
    }

    private IQueryable<User> People() => db.Set<User>().Where(u => u.TenantId == currentUser.TenantId && u.IsActive);

    private IQueryable<ChatMessage> Between(Guid other) =>
        db.Set<ChatMessage>().AsNoTracking().Where(m => (m.FromUserId == Me && m.ToUserId == other) || (m.FromUserId == other && m.ToUserId == Me));

    private static string Name(string? firstName, string? lastName, string? email) =>
        string.Join(' ', new[] { firstName, lastName }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } name ? name : email ?? string.Empty;

    private static ChatMessageDto ToDto(ChatMessage message) =>
        new(message.Id, message.FromUserId, message.ToUserId, message.Text, message.SentAt, message.ReadAt);
}
