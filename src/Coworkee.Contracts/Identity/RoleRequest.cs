namespace Coworkee.Contracts.Identity;

/// <summary>
/// A role; <paramref name="SelectableForRegistration"/> offers it in the registration wizard, <paramref name="IsDefault"/> gives it to every new user.
/// A flag left null keeps its value (false for a new role), so a client changing one flag does not overwrite a concurrent change of the other.
/// </summary>
public sealed record RoleRequest(string Name, string? Description, bool? SelectableForRegistration = null, bool? IsDefault = null);
