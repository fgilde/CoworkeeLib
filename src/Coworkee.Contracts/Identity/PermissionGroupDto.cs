namespace Coworkee.Contracts.Identity;

public sealed record PermissionGroupDto(string Name, string DisplayName, IReadOnlyList<PermissionDto> Permissions);
