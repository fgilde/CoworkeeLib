namespace Coworkee.Identity.Setup;

public sealed record SeedRole(string Name, string? Description, IReadOnlyList<string> Permissions, bool SelectableForRegistration = false)
{
    /// <summary>Every new user gets the role (<see cref="Domain.Role.IsDefault"/>).</summary>
    public bool IsDefault { get; init; }
}
