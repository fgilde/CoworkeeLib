using System.Security.Claims;

namespace Coworkee.Client.Blazor.Security;

/// <summary>Pages for the whole installation (tenants, editions, clients, ...) serve only the system organisation's administrators.</summary>
public static class SystemTenant
{
    public const string ClaimType = "system_tenant";

    /// <summary>True when the user is known to belong to another organisation; sign-ins that do not tell keep everything visible.</summary>
    public static bool IsOutside(ClaimsPrincipal? user) => user?.FindFirst(ClaimType)?.Value == "false";
}
