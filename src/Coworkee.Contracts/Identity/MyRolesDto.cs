namespace Coworkee.Contracts.Identity;

/// <summary>
/// The roles of the signed-in user. With <see cref="Enabled"/> and an <see cref="ActiveRoleId"/>, the permissions come only from that role.
/// </summary>
public sealed record MyRolesDto(bool Enabled, Guid? ActiveRoleId, IReadOnlyList<MyRoleDto> Roles);
