namespace Coworkee.Contracts.Identity;

public sealed record CustomPermissionDto(string Name, string DisplayName, string? Description, string Group);
