namespace Coworkee.Contracts.Notifications;

public sealed record NotificationDto(Guid Id, string Type, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record UnreadCountDto(int Count);
