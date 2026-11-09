namespace Coworkee.Contracts.Realtime;

/// <summary>
/// Why the session ended: "signed-out", "locked" or "password-changed" (in another session or by an administrator), "ended" (an administrator ended this one
/// session), "ended-by-user" (the user ended it in another session) or "limit" (a newer sign-in beyond the session limit). <paramref name="KeptSessionId"/>: the
/// session (<c>sid</c> of its tokens) that changed the password and goes on; a client of that session ignores the message. <paramref name="EndedSessionId"/>:
/// only this session ended; clients of other sessions ignore the message.
/// </summary>
public sealed record SessionRevokedPayload(string Reason, string? KeptSessionId = null, string? EndedSessionId = null);
