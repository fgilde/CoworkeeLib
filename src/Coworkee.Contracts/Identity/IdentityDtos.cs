namespace Coworkee.Contracts.Identity;

public sealed record RoleRefDto(Guid Id, string Name);

public sealed record UserDto(Guid Id, string UserName, string Email, string? FirstName, string? LastName, bool IsActive, IReadOnlyList<RoleRefDto> Roles);

/// <summary>A user as the admin page shows one: status, roles and groups.</summary>
public sealed record UserDetailDto(
    Guid Id, string UserName, string Email, string? FirstName, string? LastName, bool IsActive, bool EmailConfirmed, bool TwoFactorEnabled,
    DateTimeOffset? LockedUntil, DateTimeOffset? LastLoginAt, IReadOnlyList<RoleRefDto> Roles, IReadOnlyList<GroupRefDto> Groups);

public sealed record GroupRefDto(Guid Id, string Name);

/// <summary>What everyone may change about themselves; the email and the password belong to the sign-in.</summary>
public sealed record ProfileDto(string Email, string? FirstName, string? LastName, string? PhoneNumber, string? AvatarUrl = null, PostalAddress? Address = null);

public sealed record UpdateProfileRequest(string? FirstName, string? LastName, string? PhoneNumber, PostalAddress? Address = null);

public sealed record PostalAddress(string? Street, string? ZipCode, string? City, string? Country);

public sealed record CreateUserRequest(string Email, string Password, string? FirstName, string? LastName);

public sealed record UpdateUserRequest(string? FirstName, string? LastName, bool IsActive);

public sealed record IdListRequest(IReadOnlyList<Guid> Ids);

public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem);

public sealed record RoleRequest(string Name, string? Description);

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
