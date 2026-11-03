namespace Coworkee.Contracts.Notifications;

public sealed record FollowDto(Guid Id, string EntityType, Guid EntityId, string? Title, string? Link, DateTimeOffset CreatedAt);
