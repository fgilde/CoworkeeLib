namespace Coworkee.Contracts.Identity;

public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, bool SelectableForRegistration = false, bool IsDefault = false);
