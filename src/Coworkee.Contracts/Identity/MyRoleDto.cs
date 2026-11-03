namespace Coworkee.Contracts.Identity;

/// <summary><see cref="Grants"/>: the role grants the permission asked for (always true when none was asked for).</summary>
public sealed record MyRoleDto(Guid Id, string Name, string? Description, bool Grants);
