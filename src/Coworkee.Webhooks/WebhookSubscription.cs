using Coworkee.Contracts.Webhooks;
using Coworkee.Domain;

namespace Coworkee.Webhooks;

public sealed class WebhookSubscription : AuditedEntity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public required string Url { get; set; }

    public string? Description { get; set; }

    public List<string> Events { get; set; } = [];

    /// <summary>The signing secret, protected with Data Protection.</summary>
    public required string ProtectedSecret { get; set; }

    /// <summary>The secret before the last rotation, protected; deliveries are signed with it too until <see cref="PreviousSecretUntil"/>.</summary>
    public string? PreviousProtectedSecret { get; set; }

    public DateTimeOffset? PreviousSecretUntil { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Who saved the endpoint last: event data is shaped by what this user may read.</summary>
    public Guid OwnerId { get; set; }

    public int ConsecutiveFailures { get; set; }

    public DateTimeOffset? DisabledAt { get; set; }

    public bool Wants(string eventType) => eventType == WebhookEventTypes.Test || Events.Any(e => Matches(e, eventType));

    // "*" takes everything, "products.*" every event of the products
    private static bool Matches(string pattern, string eventType) =>
        pattern == WebhookEventTypes.All || pattern == eventType || (pattern.EndsWith(".*", StringComparison.Ordinal) && eventType.StartsWith(pattern[..^1], StringComparison.Ordinal));
}
