namespace Coworkee.Account;

public static class SecuritySettings
{
    public const string MaxFailedAttempts = "Account.Lockout.MaxFailedAttempts";

    public const string LockoutMinutes = "Account.Lockout.Minutes";

    public const string PasswordExpiryDays = "Account.Password.ExpiryDays";

    public const string PasswordHistory = "Account.Password.History";
}
