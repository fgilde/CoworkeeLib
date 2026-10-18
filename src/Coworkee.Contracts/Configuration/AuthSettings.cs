namespace Coworkee.Contracts.Configuration;

public static class AuthSettings
{
    /// <summary>Which sign-in forms the auth server shows (a <see cref="LoginMode"/> name); defined only when an external provider is configured.</summary>
    public const string LoginMode = "Auth.LoginMode";

    /// <summary>Whether users sign in with passkeys and manage them on the account pages; on by default.</summary>
    public const string PasskeysEnabled = "Account.Passkeys.Enabled";
}
