namespace Coworkee.Contracts.Identity;

public sealed record BffUserDto(bool IsAuthenticated, string? Name, string? Email, Guid? UserId, Guid? TenantId, IReadOnlyList<string> Roles)
{
    public static readonly BffUserDto Anonymous = new(false, null, null, null, null, []);
}
