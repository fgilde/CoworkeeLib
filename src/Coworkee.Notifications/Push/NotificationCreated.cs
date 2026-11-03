using Coworkee.Domain;

namespace Coworkee.Notifications.Push;

/// <summary>Raised for each new notification while push is configured; the notification goes to the user's devices after the commit.</summary>
public sealed record NotificationCreated(Guid NotificationId) : IDomainEvent;
