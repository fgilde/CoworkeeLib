using Coworkee.Domain;

namespace Coworkee.Infrastructure.Outbox;

[NotAudited]
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? Error { get; set; }
}
