namespace Coworkee.Contracts.Identity;

/// <summary>The own new password; the current one proves it is the user.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
