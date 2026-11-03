namespace Coworkee.Contracts.Identity;

public sealed record GrantResourcePermissionRequest(PrincipalType PrincipalType, Guid PrincipalId, Guid RoleId);
