namespace Coworkee.Contracts.Chat;

/// <param name="Name">The group name, or the other person in a direct chat.</param>
public sealed record ChatConversationDto(Guid Id, bool IsGroup, string Name, IReadOnlyList<ChatMemberDto> Members, string? LastMessage, DateTimeOffset? LastAt, int Unread);
