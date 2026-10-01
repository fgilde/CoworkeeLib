using System.Security.Claims;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

internal static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], (Delegate)AuthorizeAsync).ExcludeFromDescription();
        app.MapPost("/connect/token", TokenAsync).ExcludeFromDescription();
        app.MapMethods("/connect/endsession", [HttpMethods.Get, HttpMethods.Post], (Delegate)EndSessionAsync).ExcludeFromDescription();
    }

    private static async Task<IResult> AuthorizeAsync(HttpContext context, CoworkeeDbContext db, IOptions<AuthServerOptions> options)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("No OpenID Connect request.");
        var cookie = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!cookie.Succeeded || request.HasPromptValue(PromptValues.Login))
        {
            var parameters = context.Request.HasFormContentType ? context.Request.Form.ToList() : context.Request.Query.ToList();
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = context.Request.PathBase + context.Request.Path + QueryString.Create(parameters) },
                [IdentityConstants.ApplicationScheme]);
        }

        var userId = Guid.Parse(cookie.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var principal = await CreatePrincipalAsync(db, userId, request.GetScopes(), options.Value);
        return principal is null
            ? Results.Forbid(Error(Errors.AccessDenied, "The account is not active."), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme])
            : Results.SignIn(principal, null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> TokenAsync(HttpContext context, CoworkeeDbContext db, IOptions<AuthServerOptions> options)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("No OpenID Connect request.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Results.Forbid(Error(Errors.UnsupportedGrantType, "The grant type is not supported."), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var subject = result.Principal?.GetClaim(Claims.Subject);
        var principal = Guid.TryParse(subject, out var userId)
            ? await CreatePrincipalAsync(db, userId, result.Principal!.GetScopes(), options.Value)
            : null;
        return principal is null
            ? Results.Forbid(Error(Errors.InvalidGrant, "The account is no longer allowed to sign in."), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme])
            : Results.SignIn(principal, null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> EndSessionAsync(HttpContext context)
    {
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static async Task<ClaimsPrincipal?> CreatePrincipalAsync(CoworkeeDbContext db, Guid userId, IEnumerable<string> scopes, AuthServerOptions options)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var user = await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user is null)
        {
            return null;
        }

        var roles = await (from userRole in db.Set<IdentityUserRole<Guid>>()
                           join role in db.Set<Role>() on userRole.RoleId equals role.Id
                           where userRole.UserId == userId
                           select role.Name!).ToListAsync();

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, string.Join(' ', new[] { user.FirstName, user.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } name ? name : user.Email)
            .SetClaim("tenant", user.TenantId.ToString())
            .SetClaims(Claims.Role, [.. roles]);

        var granted = scopes.ToArray();
        identity.SetScopes(granted);
        identity.SetResources(granted.Where(options.ApiScopes.ContainsKey).Select(s => options.ApiScopes[s]).Distinct());
        identity.SetDestinations(_ => [Destinations.AccessToken, Destinations.IdentityToken]);
        return new ClaimsPrincipal(identity);
    }

    private static AuthenticationProperties Error(string error, string description) => new(new Dictionary<string, string?>
    {
        [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
    });
}
