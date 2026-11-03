using Coworkee.Domain;

namespace Coworkee.EventBus;

/// <summary>A message a handler already processed; redeliveries of it are skipped.</summary>
[NotAudited]
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }

    public required string Handler { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}
