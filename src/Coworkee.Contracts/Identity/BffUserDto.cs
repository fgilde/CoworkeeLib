namespace Coworkee.Contracts.Identity;

/// <summary>
/// The signed-in user of the BFF; <paramref name="SystemTenant"/> tells whether he belongs to the system organisation (null when the sign-in predates it),
/// <paramref name="AppUrl"/> is the web client of the app when this host is another client of it (<see cref="Configuration.BffOptions.WebAppUrl"/>),
/// <paramref name="Launcher"/> the app launcher of an AirWorks host (<c>Coworkee:AirWorks:Launcher</c>: menu or overlay),
/// <paramref name="ImpersonatorId"/> and <paramref name="ImpersonatorName"/> the user who signed in as this one ("Log in as"), <paramref name="SessionId"/> the sign-in
/// session at the auth server (<c>sid</c>; its realtime topic <c>session:{sid}</c>).
/// </summary>
public sealed record BffUserDto(
    bool IsAuthenticated, string? Name, string? Email, Guid? UserId, Guid? TenantId, IReadOnlyList<string> Roles, string? ManageUrl = null, bool? SystemTenant = null,
    string? AppUrl = null, string? Launcher = null, Guid? ImpersonatorId = null, string? ImpersonatorName = null, string? SessionId = null)
{
    public static readonly BffUserDto Anonymous = new(false, null, null, null, null, []);
}
