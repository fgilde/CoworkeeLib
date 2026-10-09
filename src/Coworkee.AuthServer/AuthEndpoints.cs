using System.Collections.Immutable;
using System.Security.Claims;
using Coworkee.Account;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

internal static class AuthEndpoints
{
    /// <summary>The consent page sends the user back to the authorization with this parameter when the user declined.</summary>
    public const string ConsentDenied = "consent_denied";

    /// <summary>"true" when the user belongs to the system organisation; the client shows the installation-wide pages only then.</summary>
    public const string SystemTenantClaim = "system_tenant";

    public static void Map(WebApplication app)
    {
        app.MapGet("/", () => Results.Redirect("/Account/Manage")).ExcludeFromDescription();
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], (Delegate)AuthorizeAsync).ExcludeFromDescription();
        app.MapPost("/connect/token", TokenAsync).ExcludeFromDescription();
        app.MapMethods("/connect/endsession", [HttpMethods.Get, HttpMethods.Post], (Delegate)EndSessionAsync).ExcludeFromDescription();
    }

    private static async Task<IResult> AuthorizeAsync(HttpContext context, CoworkeeDbContext db, IOptions<AuthServerOptions> options, PasswordPolicy policy, TimeProvider clock)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("No OpenID Connect request.");
        var cookie = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!cookie.Succeeded || request.HasPromptValue(PromptValues.Login))
        {
            return SignInFirst(context);
        }

        var user = await FindActiveAsync(db, Guid.Parse(cookie.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!));
        if (user is null)
        {
            return Results.Forbid(Error(Errors.AccessDenied, "The account is not active."), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        // signed out everywhere or locked by an administrator after this browser signed in: the sign-in no longer counts
        var stampClaim = context.RequestServices.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType;
        if (cookie.Principal.FindFirstValue(stampClaim) != user.SecurityStamp || (user.LockoutEnabled && user.LockoutEnd > clock.GetUtcNow()))
        {
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            return SignInFirst(context);
        }

        if (await policy.RequiresChangeAsync(user, context.RequestAborted))
        {
            return Results.Redirect("/Account/ChangePasswordRequired?returnUrl=" + Uri.EscapeDataString(ReturnUrl(context)));
        }

        var consent = await ConsentAsync(context, request, user);
        if (consent.Result is { } result)
        {
            return result;
        }

        var sid = cookie.Properties?.Items.TryGetValue(OwnPassword.SessionItem, out var item) == true ? item : null;
        var principal = await CreatePrincipalAsync(context.RequestServices, user, request.GetScopes(), options.Value, sid);
        if (consent.AuthorizationId is { } authorizationId)
        {
            principal.SetAuthorizationId(authorizationId);
        }

        return Results.SignIn(principal, null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>Clients with explicit consent need the user's permanent authorization for the requested scopes; the consent page records it.</summary>
    private static async Task<(IResult? Result, string? AuthorizationId)> ConsentAsync(HttpContext context, OpenIddictRequest request, User user)
    {
        var applications = context.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(request.ClientId!, context.RequestAborted)
                          ?? throw new InvalidOperationException("The client is not known.");
        if (!await applications.HasConsentTypeAsync(application, ConsentTypes.Explicit, context.RequestAborted))
        {
            return (null, null);
        }

        var authorizations = context.RequestServices.GetRequiredService<IOpenIddictAuthorizationManager>();
        var granted = await authorizations.FindAsync(user.Id.ToString(), await applications.GetIdAsync(application, context.RequestAborted), Statuses.Valid,
            AuthorizationTypes.Permanent, request.GetScopes(), context.RequestAborted).FirstOrDefaultAsync(context.RequestAborted);
        if (granted is not null)
        {
            return (null, await authorizations.GetIdAsync(granted, context.RequestAborted));
        }

        if (request.HasPromptValue(PromptValues.None) || request[ConsentDenied] is not null)
        {
            return (Results.Forbid(Error(Errors.ConsentRequired, "The user did not allow the application."), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]), null);
        }

        return (Results.Redirect("/Account/Consent?returnUrl=" + Uri.EscapeDataString(ReturnUrl(context))), null);
    }

    private static async Task<IResult> TokenAsync(HttpContext context, CoworkeeDbContext db, IOptions<AuthServerOptions> options)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("No OpenID Connect request.");
        if (request.IsClientCredentialsGrantType())
        {
            return Results.SignIn(await Clients.ServiceClient.CreatePrincipalAsync(context, request, options.Value), null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Results.Forbid(Error(Errors.UnsupportedGrantType, "The grant type is not supported."), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var user = Guid.TryParse(result.Principal?.GetClaim(Claims.Subject), out var userId) ? await FindActiveAsync(db, userId) : null;

        // a new security stamp (signed out everywhere, locked, new password) ends the refresh tokens issued before
        var stamp = result.Principal?.GetClaim(SessionStamp.ClaimType);
        var sid = result.Principal?.GetClaim(SessionStamp.SessionClaimType);
        if (user is null || (stamp is not null && !SessionStamp.IsCurrent(new UserStamp(user.SecurityStamp, user.KeptSession), stamp, sid)))
        {
            return Results.Forbid(Error(Errors.InvalidGrant, "The account is no longer allowed to sign in."), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var principal = await CreatePrincipalAsync(context.RequestServices, user, result.Principal!.GetScopes(), options.Value, sid);
        if (result.Principal!.GetAuthorizationId() is { } authorizationId)
        {
            principal.SetAuthorizationId(authorizationId);
        }

        return Results.SignIn(principal, null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> EndSessionAsync(HttpContext context)
    {
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static IResult SignInFirst(HttpContext context) =>
        Results.Challenge(new AuthenticationProperties { RedirectUri = ReturnUrl(context) }, [IdentityConstants.ApplicationScheme]);

    private static string ReturnUrl(HttpContext context)
    {
        var parameters = (context.Request.HasFormContentType ? context.Request.Form.ToList() : context.Request.Query.ToList())
            .Where(p => p.Key != ConsentDenied);
        return context.Request.PathBase + context.Request.Path + QueryString.Create(parameters);
    }

    private static async Task<User?> FindActiveAsync(CoworkeeDbContext db, Guid userId)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        return await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive);
    }

    private static async Task<ClaimsPrincipal> CreatePrincipalAsync(IServiceProvider services, User user, ImmutableArray<string> scopes, AuthServerOptions options, string? sid)
    {
        var db = services.GetRequiredService<CoworkeeDbContext>();
        List<string> roles;
        using (CurrentUserScope.Begin(new ImpersonatedUser(null, null)))
        {
            roles = await (from userRole in db.Set<IdentityUserRole<Guid>>()
                           join role in db.Set<Role>() on userRole.RoleId equals role.Id
                           where userRole.UserId == user.Id
                           select role.Name!).ToListAsync();
        }

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, string.Join(' ', new[] { user.FirstName, user.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } name ? name : user.Email)
            .SetClaim("tenant", user.TenantId.ToString())
            .SetClaim(SessionStamp.ClaimType, SessionStamp.Hash(user.SecurityStamp))
            .SetClaim(SessionStamp.SessionClaimType, sid)
            .SetClaim(SystemTenantClaim, await services.GetRequiredService<ITenantDirectory>().IsSystemTenantAsync(user.TenantId, CancellationToken.None) ? "true" : "false")
            .SetClaims(Claims.Role, [.. roles]);

        identity.SetScopes(scopes);
        identity.SetResources(await ResourcesAsync(services, scopes, options));
        identity.SetDestinations(claim => claim.Type is SessionStamp.ClaimType or SessionStamp.SessionClaimType ? [Destinations.AccessToken] : [Destinations.AccessToken, Destinations.IdentityToken]);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>The audiences of the access token: the resources of the API scopes from the configuration and of the stored scopes.</summary>
    internal static async Task<IEnumerable<string>> ResourcesAsync(IServiceProvider services, ImmutableArray<string> scopes, AuthServerOptions options)
    {
        var stored = await services.GetRequiredService<IOpenIddictScopeManager>()
            .ListResourcesAsync([.. scopes.Where(s => !options.ApiScopes.ContainsKey(s))]).ToListAsync();
        return scopes.Where(options.ApiScopes.ContainsKey).Select(s => options.ApiScopes[s]).Concat(stored).Distinct(StringComparer.Ordinal);
    }

    private static AuthenticationProperties Error(string error, string description) => new(new Dictionary<string, string?>
    {
        [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
    });
}
