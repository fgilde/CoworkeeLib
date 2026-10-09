namespace Coworkee.Contracts.Identity;

/// <summary>The signed-in user of the BFF; <paramref name="SystemTenant"/> tells whether he belongs to the system organisation (null when the sign-in predates it).</summary>
public sealed record BffUserDto(
    bool IsAuthenticated, string? Name, string? Email, Guid? UserId, Guid? TenantId, IReadOnlyList<string> Roles, string? ManageUrl = null, bool? SystemTenant = null)
{
    public static readonly BffUserDto Anonymous = new(false, null, null, null, null, []);
}
