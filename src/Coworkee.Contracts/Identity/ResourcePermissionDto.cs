namespace Coworkee.Contracts.Identity;

public sealed record ResourcePermissionDto(Guid Id, PrincipalType PrincipalType, Guid PrincipalId, Guid RoleId);
