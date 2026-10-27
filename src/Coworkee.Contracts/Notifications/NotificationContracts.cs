using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Notifications;

/// <summary>With <paramref name="Arguments"/>, title and body are English texts with {0} placeholders that the reader's language translates and fills.</summary>
/// <param name="Category">The category of the notification's type (e.g. Shipment), for the inbox filter; null for types without one.</param>
/// <param name="DoneAt">When the user marked it done; done notifications leave the open inbox.</param>
public sealed record NotificationDto(
    Guid Id, string Type, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt, IReadOnlyList<string>? Arguments = null,
    string? Category = null, DateTimeOffset? DoneAt = null);

public sealed record UnreadCountDto(int Count);

/// <summary>The inbox in numbers: not done, unread, and not done per category.</summary>
public sealed record NotificationCountsDto(int Open, int Unread, IReadOnlyDictionary<string, int> Categories);

/// <summary>Filters of the inbox; <paramref name="Done"/> null lists open and done ones.</summary>
public sealed record NotificationFilter(bool UnreadOnly = false, string? Category = null, bool? Done = null, string? Search = null);

/// <summary>Who of the possible recipients gets a type of notification: nobody, everybody who may see it, or only the followers of its entity.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationScope>))]
public enum NotificationScope
{
    Off,
    All,
    Followed,
}

/// <summary>A type of notification with the user's choice and the type's defaults.</summary>
public sealed record NotificationPreferenceDto(
    string Type, string? Category, string Name, NotificationScope Scope, bool InApp, bool Email, NotificationScope DefaultScope, bool DefaultInApp, bool DefaultEmail);

public sealed record SaveNotificationPreferenceRequest(NotificationScope Scope, bool InApp, bool Email);

/// <param name="Title">How the entity shows in the list of followed ones, e.g. its number.</param>
public sealed record FollowRequest(string? Title = null);

public sealed record FollowStateDto(bool Following, bool CanFollow);

public sealed record FollowDto(Guid Id, string EntityType, Guid EntityId, string? Title, string? Link, DateTimeOffset CreatedAt);

/// <summary>A browser's push subscription (<c>PushSubscription.toJSON()</c>): the push service address and the keys that encrypt for it.</summary>
public sealed record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth, string? Device = null);

public sealed record PushDeviceDto(Guid Id, string Endpoint, string? Device, DateTimeOffset CreatedAt);

/// <summary>The VAPID public key browsers subscribe with (null: push is not configured) and the user's devices that receive pushes.</summary>
public sealed record PushSettingsDto(string? PublicKey, IReadOnlyList<PushDeviceDto> Devices);
