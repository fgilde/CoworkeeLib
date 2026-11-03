namespace Coworkee.Contracts.Identity;

/// <summary>A user as the admin page shows one: status, profile, roles, groups and the ways to sign in.</summary>
public sealed record UserDetailDto(
    Guid Id, string UserName, string Email, string? FirstName, string? LastName, bool IsActive, bool EmailConfirmed, bool TwoFactorEnabled,
    DateTimeOffset? LockedUntil, DateTimeOffset? LastLoginAt, IReadOnlyList<RoleRefDto> Roles, IReadOnlyList<GroupRefDto> Groups, bool MustChangePassword = false,
    string? PhoneNumber = null, PostalAddress? Address = null, bool HasPassword = true, bool HasAvatar = false, IReadOnlyList<UserLoginDto>? Logins = null,
    int PasskeyCount = 0);
