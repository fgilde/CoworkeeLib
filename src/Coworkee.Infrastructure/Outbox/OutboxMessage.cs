using Coworkee.Domain;

namespace Coworkee.Infrastructure.Outbox;

[NotAudited]
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public Guid? TenantId { get; init; }

    public Guid? ActorId { get; init; }

    /// <summary>Who was signed in as <see cref="ActorId"/> ("Log in as"); the handlers run with him as <c>ICurrentUser.ImpersonatorId</c>.</summary>
    public Guid? ImpersonatorId { get; init; }

    public string? CorrelationId { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? Error { get; set; }
}
