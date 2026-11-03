namespace Coworkee.Contracts.Identity;

/// <summary>Whether the system is set up; before that the password rules the setup page shows next to the administrator password.</summary>
public sealed record SetupStatusDto(bool IsInitialized, Configuration.PasswordRules? PasswordRules = null);
