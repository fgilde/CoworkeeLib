namespace Coworkee.Contracts.Auditing;

/// <summary>
/// One entry of the security log. <paramref name="UserId"/> is whose account it concerns, <paramref name="ActorId"/> who did it when that was someone else
/// (an administrator locking the user), <paramref name="ImpersonatorId"/> who was signed in as the acting user. <paramref name="Details"/> is a JSON object.
/// </summary>
public sealed record SecurityEventDto(
    Guid Id, DateTimeOffset At, Guid? TenantId, Guid? UserId, Guid? ActorId, Guid? ImpersonatorId, string Action, bool Succeeded, string? Reason,
    string? ClientId, string? Ip, string? UserAgent, string? TraceId, string? Details);
