namespace Coworkee.Contracts.Identity;

public sealed record PermissionDto(string Name, string DisplayName, IReadOnlyList<string> Implies);
