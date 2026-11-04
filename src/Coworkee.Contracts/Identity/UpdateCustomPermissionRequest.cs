namespace Coworkee.Contracts.Identity;

/// <summary>The texts and group of a custom permission; its name stays, as grants and code refer to it.</summary>
public sealed record UpdateCustomPermissionRequest(string DisplayName, string? Description, string? Group);
