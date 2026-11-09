namespace Coworkee.Contracts.Auditing;

/// <summary>The actions of the security log; apps may write their own names.</summary>
public static class SecurityActions
{
    public const string SignIn = "SignIn";
    public const string SignOut = "SignOut";
    public const string LockedOut = "LockedOut";
    public const string Unlocked = "Unlocked";
    public const string PasswordChanged = "PasswordChanged";
    public const string EmailChanged = "EmailChanged";
    public const string TwoFactorEnabled = "TwoFactorEnabled";
    public const string TwoFactorDisabled = "TwoFactorDisabled";
    public const string PasskeyAdded = "PasskeyAdded";
    public const string PasskeyRemoved = "PasskeyRemoved";
    public const string ImpersonationStarted = "ImpersonationStarted";
    public const string ImpersonationEnded = "ImpersonationEnded";
    public const string SessionEnded = "SessionEnded";
    public const string ApiKeyCreated = "ApiKeyCreated";
    public const string ApiKeyRevoked = "ApiKeyRevoked";
    public const string ApiKeyUsed = "ApiKeyUsed";
    public const string RoleAssigned = "RoleAssigned";
    public const string RoleRemoved = "RoleRemoved";
    public const string PermissionGranted = "PermissionGranted";
    public const string PermissionRevoked = "PermissionRevoked";

    public static readonly IReadOnlyList<string> All =
    [
        SignIn, SignOut, LockedOut, Unlocked, PasswordChanged, EmailChanged, TwoFactorEnabled, TwoFactorDisabled, PasskeyAdded, PasskeyRemoved,
        ImpersonationStarted, ImpersonationEnded, SessionEnded, ApiKeyCreated, ApiKeyRevoked, ApiKeyUsed, RoleAssigned, RoleRemoved, PermissionGranted, PermissionRevoked,
    ];
}
