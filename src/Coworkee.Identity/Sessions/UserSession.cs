using Coworkee.Contracts.Identity;
using Coworkee.Domain;

namespace Coworkee.Identity.Sessions;

/// <summary>
/// One interactive sign-in of a user in a browser: the auth server's sign-in, whose id (<c>sid</c>) every token of it carries, whichever app asked for them.
/// API keys and service clients have none. An ended session's tokens and sign-in are refused at once (<see cref="Users.SessionStamp"/>); not audited, the
/// rows are the log, and <see cref="UserSessionEnded"/> tells the outbox.
/// </summary>
[Realtime(IdentityPermissions.Sessions.View)]
[NotAudited]
public sealed class UserSession : AggregateRoot, IMultiTenant, IHasRealtimeTopics
{
    /// <param name="id">The <c>sid</c> of the sign-in.</param>
    public UserSession(Guid id) => Id = id;

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The app the user signed in for; further apps of the same browser share the session.</summary>
    public string? ClientId { get; set; }

    public string? UserAgent { get; set; }

    public string? Device { get; set; }

    public string? Browser { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Who signed in as the user ("Log in as"); such sessions neither count toward nor fall under the limit.</summary>
    public Guid? ImpersonatorId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The last sign-in or token refresh, written at most every few minutes.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When the sign-in runs out without activity; the session no longer counts after it.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>One of <see cref="SessionEndReasons"/>, or why the user's security stamp changed ("signed-out", "locked", "password-changed").</summary>
    public string? EndReason { get; private set; }

    public Guid? EndedBy { get; private set; }

    /// <summary>Ended without an own signal: the browser left it itself, or a change of the security stamp told every session already.</summary>
    internal bool Quiet { get; private set; }

    public IEnumerable<string> RealtimeTopics => [Contracts.Realtime.RealtimeTopics.User(UserId)];

    public string Sid => Id.ToString("N");

    /// <summary>Not ended and not run out: such sessions count toward the limit and show as active.</summary>
    public static System.Linq.Expressions.Expression<Func<UserSession, bool>> IsActive(DateTimeOffset now) => s => s.EndedAt == null && s.ExpiresAt > now;

    public void End(DateTimeOffset now, string reason, Guid? by, bool quiet = false)
    {
        if (EndedAt is not null)
        {
            return;
        }

        EndedAt = now;
        EndReason = reason;
        EndedBy = by;
        Quiet = quiet;
        Raise(new UserSessionEnded(Id, UserId, TenantId, reason, by, ImpersonatorId));
    }

    public UserSessionDto ToDto(string? currentSid = null) =>
        new(Id, UserId, ClientId, Device, Browser, IpAddress, ImpersonatorId, CreatedAt, LastSeenAt, ExpiresAt, EndedAt, EndReason, currentSid == Sid);
}

public static class SessionEndReasons
{
    /// <summary>An administrator ended the session.</summary>
    public const string Ended = "ended";

    /// <summary>The user ended it in another of his sessions.</summary>
    public const string EndedByUser = "ended-by-user";

    /// <summary>A newer sign-in went beyond the session limit.</summary>
    public const string Limit = "limit";

    /// <summary>The user signed out in this browser.</summary>
    public const string LoggedOut = "logged-out";

    /// <summary>The browser signed in again, as someone else or after "Log in as".</summary>
    public const string Replaced = "replaced";
}
