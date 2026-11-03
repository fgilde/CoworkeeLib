namespace Coworkee.Contracts.Notifications;

public sealed record SaveNotificationPreferenceRequest(NotificationScope Scope, bool InApp, bool Email);
