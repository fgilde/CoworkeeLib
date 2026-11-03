namespace Coworkee.Contracts.Identity;

/// <summary>Null acts with all roles again.</summary>
public sealed record SetActiveRoleRequest(Guid? RoleId);
