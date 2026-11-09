using Coworkee.Domain;

namespace Coworkee.Identity.Sessions;

/// <summary>
/// A session ended (<see cref="UserSession.End"/>), written to the outbox with the session's row; <see cref="UserSessionEndedHandler"/> writes it to the security log, an app handles it with an
/// <c>IDomainEventHandler&lt;UserSessionEnded&gt;</c>; <paramref name="EndedBy"/> is null when the sign-in ended it (limit, sign-out).
/// </summary>
public sealed record UserSessionEnded(Guid SessionId, Guid UserId, Guid TenantId, string Reason, Guid? EndedBy, Guid? ImpersonatorId) : IDomainEvent;
