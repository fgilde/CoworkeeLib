using System.Security.Claims;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Security;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => CurrentUserScope.Current is { } ambient
        ? ambient.UserId
        : ParseGuid(Principal?.FindFirstValue("sub") ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier));

    public Guid? TenantId => CurrentUserScope.Current is { } ambient ? ambient.TenantId : ParseGuid(Principal?.FindFirstValue("tenant"));

    public bool IsAuthenticated => CurrentUserScope.Current?.IsAuthenticated ?? Principal?.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Roles => CurrentUserScope.Current?.Roles
        ?? Principal?.Claims.Where(c => c.Type is "role" or ClaimTypes.Role).Select(c => c.Value).ToArray()
        ?? [];

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
