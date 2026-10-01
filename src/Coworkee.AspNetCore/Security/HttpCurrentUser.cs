using System.Security.Claims;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Security;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => ParseGuid(Principal?.FindFirstValue("sub") ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier));

    public Guid? TenantId => ParseGuid(Principal?.FindFirstValue("tenant"));

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Roles =>
        Principal?.Claims.Where(c => c.Type is "role" or ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
