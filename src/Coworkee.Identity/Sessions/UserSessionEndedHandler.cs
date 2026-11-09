using Coworkee.Application.Auditing;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Auditing;

namespace Coworkee.Identity.Sessions;

/// <summary>Every ended session is a <see cref="SecurityActions.SessionEnded"/> entry of the security log, with why and by whom it ended.</summary>
internal sealed class UserSessionEndedHandler(ISecurityLog securityLog) : IDomainEventHandler<UserSessionEnded>
{
    public Task HandleAsync(UserSessionEnded domainEvent, CancellationToken cancellationToken) =>
        securityLog.WriteAsync(new SecurityLogEntry(SecurityActions.SessionEnded, domainEvent.UserId, domainEvent.TenantId, Reason: domainEvent.Reason,
            Details: new { sessionId = domainEvent.SessionId, endedBy = domainEvent.EndedBy }, ImpersonatorId: domainEvent.ImpersonatorId), cancellationToken);
}
