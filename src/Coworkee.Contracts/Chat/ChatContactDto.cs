namespace Coworkee.Contracts.Chat;

public sealed record ChatContactDto(Guid UserId, string Name, string? LastMessage, DateTimeOffset? LastAt, int Unread);
