namespace Coworkee.Contracts.Realtime;

/// <summary>
/// "Log in as" <paramref name="TargetId"/> by <paramref name="ImpersonatorId"/> ended: stopped, expired or revoked at the auth server. Sent only on the
/// impersonating session's topic (<see cref="RealtimeTopics.Session"/>), never to the target's own sessions.
/// </summary>
public sealed record ImpersonationEndedPayload(Guid TargetId, Guid ImpersonatorId);
