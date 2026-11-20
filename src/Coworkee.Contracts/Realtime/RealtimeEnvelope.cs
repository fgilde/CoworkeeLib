using System.Text.Json;

namespace Coworkee.Contracts.Realtime;

/// <summary>
/// One realtime message. The publisher fills the metadata: <see cref="Id"/> (unique per message), <see cref="At"/> (when it was sent),
/// <see cref="Actor"/> (who caused it; null for the system) and <see cref="CorrelationId"/> (the trace of the request that caused it). Older
/// clients ignore them.
/// </summary>
public sealed record RealtimeEnvelope(string Topic, string Type, JsonElement Payload)
{
    public Guid? Id { get; init; }

    public DateTimeOffset? At { get; init; }

    public ActorInfo? Actor { get; init; }

    public string? CorrelationId { get; init; }
}
