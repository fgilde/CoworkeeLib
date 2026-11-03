namespace Coworkee.Contracts.Identity;

/// <summary>Locks a user out until <paramref name="Until"/>, or for good when it is null.</summary>
public sealed record LockUserRequest(DateTimeOffset? Until);
