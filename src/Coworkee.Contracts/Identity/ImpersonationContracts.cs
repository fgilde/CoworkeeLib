namespace Coworkee.Contracts.Identity;

/// <summary>Proof for the auth server that the signed-in user may sign in as another user; valid for a minute and for this user only.</summary>
public sealed record ImpersonationTicketDto(string Ticket);

/// <summary>Claims of the sign-in and the tokens while a user is signed in as another user: the target is the subject, these name who acts.</summary>
public static class ImpersonationClaims
{
    public const string Impersonator = "impersonator";

    public const string ImpersonatorName = "impersonator_name";

    /// <summary>Hash of the impersonator's security stamp at the start: signing out everywhere, a new password or a lock ends the impersonation.</summary>
    public const string ImpersonatorStamp = "impersonator_stamp";

    /// <summary>When the impersonation started, Unix seconds; it ends after <c>AuthServerOptions.MaxImpersonationDuration</c>.</summary>
    public const string Started = "impersonation_started";
}
