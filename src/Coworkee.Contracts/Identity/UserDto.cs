namespace Coworkee.Contracts.Identity;

public sealed record UserDto(Guid Id, string UserName, string Email, string? FirstName, string? LastName, bool IsActive, IReadOnlyList<RoleRefDto> Roles);
