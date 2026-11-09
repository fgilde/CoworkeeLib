namespace Coworkee.Contracts.Identity;

/// <summary>The active sessions of a user and his own limit: null takes the organisation's setting, 0 is unlimited.</summary>
public sealed record UserSessionsDto(int? MaxSessions, IReadOnlyList<UserSessionDto> Sessions);
