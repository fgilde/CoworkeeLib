using System.Security.Claims;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Security;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => CurrentUserScope.Current is { } ambient
        ? ambient.UserId
        : ServiceClient is null ? ParseGuid(Principal?.FindFirstValue("sub") ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier)) : null;

    public Guid? TenantId => CurrentUserScope.Current is { } ambient ? ambient.TenantId : ParseGuid(Principal?.FindFirstValue("tenant"));

    public bool IsAuthenticated => CurrentUserScope.Current?.IsAuthenticated ?? Principal?.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Roles => CurrentUserScope.Current?.Roles
        ?? Principal?.Claims.Where(c => c.Type is "role" or ClaimTypes.Role).Select(c => c.Value).ToArray()
        ?? [];

    public string? ClientId => CurrentUserScope.Current is { } ambient ? ambient.ClientId : ServiceClient;

    public IReadOnlyCollection<string> ClientPermissions => CurrentUserScope.Current is { } ambient
        ? ambient.ClientPermissions
        : ServiceClient is null ? [] : Principal!.FindAll("permission").Select(c => c.Value).ToArray();

    // tokens of the client credentials grant name the client as subject
    private string? ServiceClient => Principal?.FindFirstValue("client_id") is { } client && Principal.FindFirstValue("sub") == client ? client : null;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
