namespace Coworkee.Contracts.Identity;

public sealed record RoleRefDto(Guid Id, string Name);

public sealed record UserDto(Guid Id, string UserName, string Email, string? FirstName, string? LastName, bool IsActive, IReadOnlyList<RoleRefDto> Roles);

/// <summary>A user as the admin page shows one: status, profile, roles, groups and the ways to sign in.</summary>
public sealed record UserDetailDto(
    Guid Id, string UserName, string Email, string? FirstName, string? LastName, bool IsActive, bool EmailConfirmed, bool TwoFactorEnabled,
    DateTimeOffset? LockedUntil, DateTimeOffset? LastLoginAt, IReadOnlyList<RoleRefDto> Roles, IReadOnlyList<GroupRefDto> Groups, bool MustChangePassword = false,
    string? PhoneNumber = null, PostalAddress? Address = null, bool HasPassword = true, bool HasAvatar = false, IReadOnlyList<UserLoginDto>? Logins = null);

public sealed record GroupRefDto(Guid Id, string Name);

/// <summary>What everyone may change about themselves; the email and the password belong to the sign-in.</summary>
public sealed record ProfileDto(string Email, string? FirstName, string? LastName, string? PhoneNumber, string? AvatarUrl = null, PostalAddress? Address = null, bool HasPassword = true);

public sealed record UpdateProfileRequest(string? FirstName, string? LastName, string? PhoneNumber, PostalAddress? Address = null);

public sealed record PostalAddress(string? Street, string? ZipCode, string? City, string? Country);

/// <summary>
/// A new user; <paramref name="MustChangePassword"/> makes the first sign-in ask for an own password.
/// Without <paramref name="Password"/> the user signs in after choosing one from an invitation or reset mail.
/// </summary>
public sealed record CreateUserRequest(
    string Email, string? Password, string? FirstName, string? LastName, bool MustChangePassword = false, IReadOnlyList<Guid>? RoleIds = null, bool IsActive = true);

/// <summary>Changes a user; the optional values left null stay as they are, empty texts clear them.</summary>
public sealed record UpdateUserRequest(
    string? FirstName, string? LastName, bool IsActive, bool? MustChangePassword = null,
    string? UserName = null, string? PhoneNumber = null, PostalAddress? Address = null, bool? EmailConfirmed = null);

/// <summary>Locks a user out until <paramref name="Until"/>, or for good when it is null.</summary>
public sealed record LockUserRequest(DateTimeOffset? Until);

public sealed record IdListRequest(IReadOnlyList<Guid> Ids);

public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, bool SelectableForRegistration = false);

/// <summary>A role; <paramref name="SelectableForRegistration"/> offers it in the registration wizard.</summary>
public sealed record RoleRequest(string Name, string? Description, bool SelectableForRegistration = false);

public sealed record GroupDto(Guid Id, string Name, string? Description, IReadOnlyList<Guid> MemberIds, IReadOnlyList<Guid> RoleIds);

public sealed record GroupRequest(string Name, string? Description);

public sealed record PermissionDto(string Name, string DisplayName, IReadOnlyList<string> Implies);

public sealed record PermissionGroupDto(string Name, string DisplayName, IReadOnlyList<PermissionDto> Permissions);

public sealed record NameListRequest(IReadOnlyList<string> Names);

public sealed record ResourcePermissionDto(Guid Id, PrincipalType PrincipalType, Guid PrincipalId, Guid RoleId);

public sealed record GrantResourcePermissionRequest(PrincipalType PrincipalType, Guid PrincipalId, Guid RoleId);

public sealed record SetupStatusDto(bool IsInitialized);

public sealed record CompleteSetupRequest(
    string SetupToken, string TenantName, string AdminEmail, string AdminPassword, string? AdminFirstName, string? AdminLastName,
    IReadOnlyDictionary<string, string?>? Settings = null,
    Guid? ThemeId = null);

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SetupCheckStatus>))]
public enum SetupCheckStatus
{
    Ok,
    Warning,
    Error,
}

public sealed record SetupCheckDto(string Name, SetupCheckStatus Status, string? Message);

public sealed record SetupResultDto(Guid TenantId, Guid AdminUserId);

public sealed record DeleteAccountRequest(string Email);
