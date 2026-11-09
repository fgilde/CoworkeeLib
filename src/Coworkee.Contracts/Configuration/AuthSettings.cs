namespace Coworkee.Contracts.Configuration;

public static class AuthSettings
{
    /// <summary>Which sign-in forms the auth server shows (a <see cref="LoginMode"/> name); defined only when an external provider is configured.</summary>
    public const string LoginMode = "Auth.LoginMode";

    /// <summary>Whether users sign in with passkeys and manage them on the account pages; on by default.</summary>
    public const string PasskeysEnabled = "Account.Passkeys.Enabled";

    /// <summary>How many sessions a user may have at once; 0 (the default) is unlimited. A user's own limit wins (<c>User.MaxSessions</c>).</summary>
    public const string MaxSessionsPerUser = "Account.Sessions.MaxPerUser";

    /// <summary>What a sign-in beyond the limit does: one of <see cref="SessionLimitModes"/>.</summary>
    public const string SessionLimitMode = "Account.Sessions.LimitMode";
}

public static class SessionLimitModes
{
    /// <summary>The new sign-in goes on, the oldest sessions end (the default).</summary>
    public const string EndOldest = "EndOldest";

    /// <summary>The new sign-in is refused until a session ends.</summary>
    public const string Refuse = "Refuse";

    public static readonly string[] All = [EndOldest, Refuse];
}
