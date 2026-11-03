using System.Text.Json;
using Coworkee.Contracts.Realtime;
using Coworkee.Domain;
using Coworkee.Infrastructure.Outbox;

namespace Coworkee.Identity.Users;

/// <summary>
/// The auth server ended "Log in as" (stop, expiry, revocation) of the sign-in <paramref name="SessionId"/>; it writes the event to the outbox, the host that
/// processes the outbox (the api) puts <see cref="RealtimeEventTypes.ImpersonationEnded"/> on that session's topic, so the target's own sessions learn nothing.
/// </summary>
public sealed record ImpersonationEnded(Guid TargetId, Guid ImpersonatorId, string SessionId) : IDomainEvent
{
    public OutboxMessage ToOutbox(DateTimeOffset now) => new()
    {
        Type = typeof(ImpersonationEnded).AssemblyQualifiedName!,
        Payload = JsonSerializer.Serialize(this),
        OccurredAt = now,
        ActorId = ImpersonatorId,
    };
}
