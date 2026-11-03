namespace Coworkee.Contracts.Notifications;

/// <summary>With <paramref name="Arguments"/>, title and body are English texts with {0} placeholders that the reader's language translates and fills.</summary>
/// <param name="Category">The category of the notification's type (e.g. Shipment), for the inbox filter; null for types without one.</param>
/// <param name="DoneAt">When the user marked it done; done notifications leave the open inbox.</param>
public sealed record NotificationDto(
    Guid Id, string Type, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt, IReadOnlyList<string>? Arguments = null,
    string? Category = null, DateTimeOffset? DoneAt = null);
