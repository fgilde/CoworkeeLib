namespace Coworkee.Contracts.Notifications;

/// <summary>With <paramref name="Arguments"/>, title and body are English texts with {0} placeholders that the reader's language translates and fills.</summary>
public sealed record NotificationDto(
    Guid Id, string Type, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt, IReadOnlyList<string>? Arguments = null);

public sealed record UnreadCountDto(int Count);

/// <summary>A browser's push subscription (<c>PushSubscription.toJSON()</c>): the push service address and the keys that encrypt for it.</summary>
public sealed record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth, string? Device = null);

public sealed record PushDeviceDto(Guid Id, string Endpoint, string? Device, DateTimeOffset CreatedAt);

/// <summary>The VAPID public key browsers subscribe with (null: push is not configured) and the user's devices that receive pushes.</summary>
public sealed record PushSettingsDto(string? PublicKey, IReadOnlyList<PushDeviceDto> Devices);
