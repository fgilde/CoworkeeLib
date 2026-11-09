namespace Coworkee.Contracts.Identity;

/// <summary>The sessions a user may have at once: null takes the organisation's setting, 0 is unlimited.</summary>
public sealed record SessionLimitRequest(int? MaxSessions);
