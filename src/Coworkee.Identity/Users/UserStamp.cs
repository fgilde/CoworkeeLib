namespace Coworkee.Identity.Users;

/// <param name="EndedSessions">The ended sessions (<c>sid</c>s, space-separated) whose tokens may still be around; the API refuses them.</param>
public sealed record UserStamp(string? SecurityStamp, string? KeptSession, string? EndedSessions = null);
