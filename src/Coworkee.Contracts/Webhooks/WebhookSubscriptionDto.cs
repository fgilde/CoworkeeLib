namespace Coworkee.Contracts.Webhooks;

public sealed record WebhookSubscriptionDto(
    Guid Id, string Url, string? Description, IReadOnlyList<string> Events, bool IsActive, int ConsecutiveFailures, DateTimeOffset? DisabledAt, DateTimeOffset CreatedAt)
{
    /// <summary>After a rotation, deliveries carry the old secret's signature too until then.</summary>
    public DateTimeOffset? PreviousSecretUntil { get; init; }
}
