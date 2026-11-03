namespace Coworkee.Contracts.Auditing;

/// <summary><paramref name="ImpersonatorId"/>: only changes someone made while signed in as another user ("Log in as").</summary>
public sealed record AuditQuery(
    string? EntityType = null, string? EntityId = null, Guid? ActorId = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 25,
    Guid? ImpersonatorId = null);
