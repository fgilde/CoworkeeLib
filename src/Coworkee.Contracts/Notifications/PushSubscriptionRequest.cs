namespace Coworkee.Contracts.Notifications;

/// <summary>A browser's push subscription (<c>PushSubscription.toJSON()</c>): the push service address and the keys that encrypt for it.</summary>
public sealed record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth, string? Device = null);
