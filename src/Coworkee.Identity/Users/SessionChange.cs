namespace Coworkee.Identity.Users;

/// <summary>
/// A user whose security stamp changed; <paramref name="Reason"/> is "locked", "password-changed" or "signed-out", <paramref name="KeptSessionId"/> the session
/// that changed its own password and goes on. With <paramref name="EndedSessionId"/> only that session ended (<see cref="Sessions.SessionEndReasons"/>).
/// </summary>
public sealed record SessionChange(Guid UserId, string Reason, string? KeptSessionId = null, string? EndedSessionId = null);
