using System.Diagnostics;
using System.Text.Json;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Outbox;

namespace Coworkee.Webhooks;

internal static class WebhookOutbox
{
    /// <param name="anonymous">An anonymous record: the message names no actor and carries no trace id.</param>
    public static OutboxMessage Message(object payload, Guid? tenantId, ICurrentUser actor, TimeProvider clock, bool anonymous = false) => new()
    {
        Type = payload.GetType().AssemblyQualifiedName!,
        Payload = JsonSerializer.Serialize(payload, payload.GetType()),
        OccurredAt = clock.GetUtcNow(),
        TenantId = tenantId,
        ActorId = anonymous ? null : actor.UserId,
        ImpersonatorId = anonymous ? null : actor.ImpersonatorId,
        CorrelationId = anonymous ? null : Activity.Current?.TraceId.ToHexString(),
    };
}
