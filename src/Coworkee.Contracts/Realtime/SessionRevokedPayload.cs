namespace Coworkee.Contracts.Realtime;

/// <summary>
/// Why the session ended: "signed-out", "locked" or "password-changed" (in another session or by an administrator). <paramref name="KeptSessionId"/>: the session
/// (<c>sid</c> of its tokens) that changed the password and goes on; a client of that session ignores the message.
/// </summary>
public sealed record SessionRevokedPayload(string Reason, string? KeptSessionId = null);
