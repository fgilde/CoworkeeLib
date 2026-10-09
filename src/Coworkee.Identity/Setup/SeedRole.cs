namespace Coworkee.Identity.Setup;

public sealed record SeedRole(string Name, string? Description, IReadOnlyList<string> Permissions, bool SelectableForRegistration = false);
