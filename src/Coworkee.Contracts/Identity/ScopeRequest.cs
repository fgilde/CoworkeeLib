namespace Coworkee.Contracts.Identity;

public sealed record ScopeRequest(string Name, string? DisplayName, string? Description, IReadOnlyList<string> Resources);
