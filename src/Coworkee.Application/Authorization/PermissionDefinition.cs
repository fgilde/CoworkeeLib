namespace Coworkee.Application.Authorization;

public sealed record PermissionDefinition(string Name, string Group, string DisplayName, IReadOnlyList<string> Implies);
