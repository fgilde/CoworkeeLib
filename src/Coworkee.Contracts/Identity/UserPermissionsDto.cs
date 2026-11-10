namespace Coworkee.Contracts.Identity;

/// <summary>
/// What a user holds through all his roles (whatever role he picked as active one, also while deactivated), each permission with where it comes from,
/// and the permissions granted to him directly.
/// </summary>
public sealed record UserPermissionsDto(IReadOnlyList<string> Granted, IReadOnlyList<UserPermissionDto> Permissions);

/// <summary>One permission a user holds and every source that gives it to him.</summary>
public sealed record UserPermissionDto(string Name, IReadOnlyList<PermissionSourceDto> Sources);
