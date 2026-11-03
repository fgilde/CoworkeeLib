namespace Coworkee.Contracts.Chat;

/// <summary><see cref="ReaderId"/> read the conversation up to <see cref="ReadAt"/>; <see cref="SenderId"/> is the other person of a direct chat, else empty.</summary>
public sealed record ChatReadDto(Guid ReaderId, Guid SenderId, DateTimeOffset ReadAt)
{
    public Guid ConversationId { get; init; }
}
