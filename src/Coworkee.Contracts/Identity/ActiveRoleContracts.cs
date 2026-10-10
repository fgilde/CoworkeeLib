namespace Coworkee.Contracts.Identity;

/// <summary>
/// The roles of the signed-in user. With <see cref="Enabled"/> and an <see cref="ActiveRoleId"/>, the permissions come only from that role.
/// </summary>
public sealed record MyRolesDto(bool Enabled, Guid? ActiveRoleId, IReadOnlyList<MyRoleDto> Roles);

/// <summary><see cref="Grants"/>: the role grants the permission asked for (always true when none was asked for).</summary>
public sealed record MyRoleDto(Guid Id, string Name, string? Description, bool Grants);

/// <summary>Null acts with all roles again.</summary>
public sealed record SetActiveRoleRequest(Guid? RoleId);
