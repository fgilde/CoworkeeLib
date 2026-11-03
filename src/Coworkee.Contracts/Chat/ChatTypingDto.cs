namespace Coworkee.Contracts.Chat;

public sealed record ChatTypingDto(Guid ConversationId, Guid UserId);
