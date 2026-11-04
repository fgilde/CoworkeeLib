namespace Coworkee.Contracts.Identity;

/// <summary>A new custom permission; <paramref name="Name"/> starts with <see cref="CustomPermissionNames.Prefix"/>, a null <paramref name="Group"/> is <see cref="CustomPermissionNames.DefaultGroup"/>.</summary>
public sealed record CreateCustomPermissionRequest(string Name, string DisplayName, string? Description, string? Group);
