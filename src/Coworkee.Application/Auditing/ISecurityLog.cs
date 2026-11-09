namespace Coworkee.Application.Auditing;

/// <summary>
/// Writes an entry to the security log (sign-ins, sessions, credentials, access). Address, browser, client, trace and the signed-in user (and who signed in
/// as him) come from the current request; the entry is saved at once in a scope of its own, so it stays when the caller's unit of work does not.
/// </summary>
public interface ISecurityLog
{
    Task WriteAsync(SecurityLogEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// <paramref name="Action"/>: one of <c>SecurityActions</c> or an own name. <paramref name="UserId"/> is whose account it concerns (null: the current user),
/// <paramref name="TenantId"/> his organisation (null: the current one), <paramref name="ImpersonatorId"/> who is signed in as him (null: the current
/// impersonator). <paramref name="Details"/> is serialized as JSON; keep secrets out of it.
/// </summary>
public sealed record SecurityLogEntry(
    string Action, Guid? UserId = null, Guid? TenantId = null, bool Succeeded = true, string? Reason = null, object? Details = null, Guid? ImpersonatorId = null);
