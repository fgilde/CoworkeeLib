using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer.Clients;

/// <summary>
/// A client with the client credentials grant calls APIs on its own. It acts in the system organisation with the permissions of the
/// roles and the permission list stored with the client; its access token names the client as subject, the roles and those permissions.
/// </summary>
internal static class ServiceClient
{
    public const string PermissionClaim = "permission";

    private const string RolesProperty = "coworkee_roles";
    private const string PermissionsProperty = "coworkee_permissions";

    public static IReadOnlyList<Guid> Roles(ImmutableDictionary<string, JsonElement> properties) => Read<Guid>(properties, RolesProperty);

    public static IReadOnlyList<string> Permissions(ImmutableDictionary<string, JsonElement> properties) => Read<string>(properties, PermissionsProperty);

    public static void Write(OpenIddictApplicationDescriptor descriptor, IReadOnlyList<Guid> roles, IReadOnlyList<string> permissions)
    {
        descriptor.Properties.Remove(RolesProperty);
        descriptor.Properties.Remove(PermissionsProperty);
        if (roles.Count > 0)
        {
            descriptor.Properties[RolesProperty] = JsonSerializer.SerializeToElement(roles);
        }

        if (permissions.Count > 0)
        {
            descriptor.Properties[PermissionsProperty] = JsonSerializer.SerializeToElement(permissions);
        }
    }

    public static async Task<ClaimsPrincipal> CreatePrincipalAsync(HttpContext context, OpenIddictRequest request, AuthServerOptions options)
    {
        var services = context.RequestServices;
        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(request.ClientId!, context.RequestAborted)
                          ?? throw new InvalidOperationException("The client is not known.");
        var properties = await applications.GetPropertiesAsync(application, context.RequestAborted);
        var tenantId = await services.GetRequiredService<ITenantDirectory>().GetSystemTenantIdAsync(context.RequestAborted);
        var roleIds = Roles(properties);
        List<string> roles;
        using (CurrentUserScope.Begin(new ImpersonatedUser(null, null)))
        {
            roles = await services.GetRequiredService<CoworkeeDbContext>().Set<Role>()
                .Where(r => roleIds.Contains(r.Id) && r.TenantId == tenantId && !r.IsSystem).Select(r => r.Name!).ToListAsync(context.RequestAborted);
        }

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, request.ClientId)
            .SetClaim(Claims.ClientId, request.ClientId)
            .SetClaim(Claims.Name, await applications.GetDisplayNameAsync(application, context.RequestAborted) ?? request.ClientId)
            .SetClaim("tenant", tenantId?.ToString())
            .SetClaims(Claims.Role, [.. roles])
            .SetClaims(PermissionClaim, [.. Permissions(properties)]);
        identity.SetScopes(request.GetScopes());
        identity.SetResources(await AuthEndpoints.ResourcesAsync(services, request.GetScopes(), options));
        identity.SetDestinations(_ => [Destinations.AccessToken]);
        return new ClaimsPrincipal(identity);
    }

    private static IReadOnlyList<T> Read<T>(ImmutableDictionary<string, JsonElement> properties, string name) =>
        properties.TryGetValue(name, out var value) ? value.Deserialize<T[]>() ?? [] : [];
}
