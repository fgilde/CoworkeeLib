using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Webhooks;

public static class WebhookPermissions
{
    public const string GroupName = "Webhooks";

    /// <summary>Register, change and test the organisation's webhook endpoints and read their delivery log.</summary>
    public const string Manage = "Webhooks.Manage";
}

public static class WebhookEventTypes
{
    /// <summary>Sent by "Send test event", whatever events the endpoint chose.</summary>
    public const string Test = "webhook.test";

    /// <summary>The endpoint takes every event.</summary>
    public const string All = "*";
}

[JsonConverter(typeof(JsonStringEnumConverter<WebhookDeliveryStatus>))]
public enum WebhookDeliveryStatus
{
    Pending,
    Succeeded,
    Failed,
}

/// <param name="Secret">Null keeps the stored secret, a new endpoint gets a generated one.</param>
public sealed record WebhookRequest(string Url, IReadOnlyList<string> Events, string? Description = null, bool IsActive = true, string? Secret = null);

public sealed record WebhookSubscriptionDto(
    Guid Id, string Url, string? Description, IReadOnlyList<string> Events, bool IsActive, int ConsecutiveFailures, DateTimeOffset? DisabledAt, DateTimeOffset CreatedAt);

public sealed record WebhookSecretDto(Guid Id, string Secret);

public sealed record WebhookEventTypeDto(string Name, string? Description);

public sealed record WebhookDeliveryDto(
    Guid Id, Guid SubscriptionId, Guid EventId, string EventType, WebhookDeliveryStatus Status, int Attempts, DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt, DateTimeOffset? NextAttemptAt, int? ResponseStatus, long? DurationMs, string? Error, string? Request, string? Response);
