namespace Coworkee.Contracts.Notifications;

/// <summary>The inbox in numbers: not done, unread, and not done per category.</summary>
public sealed record NotificationCountsDto(int Open, int Unread, IReadOnlyDictionary<string, int> Categories);
