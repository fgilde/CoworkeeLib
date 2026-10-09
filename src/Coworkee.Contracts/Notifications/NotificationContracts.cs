namespace Coworkee.Contracts.Notifications;

/// <summary>With <paramref name="Arguments"/>, title and body are English texts with {0} placeholders that the reader's language translates and fills.</summary>
public sealed record NotificationDto(
    Guid Id, string Type, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt, IReadOnlyList<string>? Arguments = null);

public sealed record UnreadCountDto(int Count);
