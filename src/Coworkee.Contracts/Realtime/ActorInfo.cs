namespace Coworkee.Contracts.Realtime;

/// <summary>Who caused an event: the user (null for the system), his display name when known, and who signed in as him ("Log in as").</summary>
public sealed record ActorInfo(Guid? UserId, string? Name = null, Guid? ImpersonatorId = null);
