namespace Coworkee.Contracts.Identity;

/// <summary>
/// One sign-in of a user in a browser or app (the <c>sid</c> of its tokens). <paramref name="ImpersonatorId"/> is set when someone signed in as the user
/// ("Log in as"); <paramref name="Current"/> marks the caller's own session in "My sessions".
/// </summary>
public sealed record UserSessionDto(
    Guid Id, Guid UserId, string? ClientId, string? Device, string? Browser, string? IpAddress, Guid? ImpersonatorId,
    DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt, DateTimeOffset? EndedAt, string? EndReason, bool Current = false);
