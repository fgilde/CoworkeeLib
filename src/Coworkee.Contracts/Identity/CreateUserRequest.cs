namespace Coworkee.Contracts.Identity;

/// <summary>
/// A new user; <paramref name="MustChangePassword"/> makes the first sign-in ask for an own password.
/// Without <paramref name="Password"/> the user signs in after choosing one from an invitation or reset mail. <paramref name="GroupIds"/> are the groups
/// he joins; an administrator limited to his groups names at least one of them.
/// </summary>
public sealed record CreateUserRequest(
    string Email, string? Password, string? FirstName, string? LastName, bool MustChangePassword = false, IReadOnlyList<Guid>? RoleIds = null, bool IsActive = true,
    IReadOnlyList<Guid>? GroupIds = null);
