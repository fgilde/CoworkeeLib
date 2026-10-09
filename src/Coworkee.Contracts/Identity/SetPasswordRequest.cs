namespace Coworkee.Contracts.Identity;

/// <summary>An administrator sets a user's password.</summary>
public sealed record SetPasswordRequest(string Password, bool MustChangePassword);
