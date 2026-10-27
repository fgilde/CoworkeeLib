using System.Security.Claims;
using System.Text.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

/// <summary>
/// "Log in as": the sign-in of the auth server names the target as user and the impersonator in extra claims, which go into the tokens.
/// Refresh tokens hang on an authorization of that sign-in (its session id), so "Back to my account" revokes exactly them.
/// </summary>
internal static class AuthImpersonation
{
    /// <summary>Property of the authorizations created while impersonating: the session id of the sign-in.</summary>
    public const string SessionProperty = "coworkee_impersonation";

    public static bool IsImpersonating(ClaimsPrincipal? principal) => principal?.FindFirst(ImpersonationClaims.Impersonator) is not null;

    public static void Conventions(RazorPagesOptions pages)
    {
        var filter = new NotWhileImpersonatingFilter();
        pages.Conventions.AddFolderApplicationModelConvention("/Account/Manage", model => model.Filters.Add(filter));
        pages.Conventions.AddPageApplicationModelConvention("/Account/ChangePasswordRequired", model => model.Filters.Add(filter));
    }

    // the cookie's sign-in is renewed when its security stamp is checked; the impersonation has to survive that
    public static Task KeepClaims(SecurityStampRefreshingPrincipalContext context)
    {
        var claims = context.CurrentPrincipal?.Claims.Where(c => c.Type is ImpersonationClaims.Impersonator or ImpersonationClaims.ImpersonatorName).ToList() ?? [];
        if (claims.Count > 0 && context.NewPrincipal?.Identities.FirstOrDefault() is { } identity)
        {
            identity.AddClaims(claims.Select(c => new Claim(c.Type, c.Value)));
        }

        return Task.CompletedTask;
    }

    /// <summary>A local path, or an address on the origin of a client's redirect address; nothing else, so the pages never redirect off to strangers.</summary>
    public static async Task<bool> MayRedirectToAsync(HttpContext context, IUrlHelper url, string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            return false;
        }

        if (url.IsLocalUrl(returnUrl))
        {
            return true;
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var target) || target.Scheme is not ("https" or "http") || target.UserInfo.Length > 0)
        {
            return false;
        }

        var options = context.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        return options.Clients.SelectMany(c => c.RedirectUris).Concat((await CoworkeeAuthServerModule.StoredClientsAsync(context)).Uris)
            .Any(u => Uri.TryCreate(u, UriKind.Absolute, out var registered)
                      && Uri.Compare(registered, target, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0);
    }

    public static async Task<string> CreateAuthorizationAsync(IServiceProvider services, OpenIddictRequest request, ClaimsPrincipal principal, string? sid, DateTimeOffset now)
    {
        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
        var application = await applications.FindByClientIdAsync(request.ClientId!) ?? throw new InvalidOperationException("The client is not known.");
        var descriptor = new OpenIddictAuthorizationDescriptor
        {
            ApplicationId = await applications.GetIdAsync(application),
            CreationDate = now,
            Principal = principal,
            Status = Statuses.Valid,
            Subject = principal.GetClaim(Claims.Subject),
            Type = AuthorizationTypes.AdHoc,
        };
        descriptor.Scopes.UnionWith(request.GetScopes());
        descriptor.Properties[SessionProperty] = JsonSerializer.SerializeToElement(sid ?? string.Empty);
        return (await authorizations.GetIdAsync(await authorizations.CreateAsync(descriptor)))!;
    }

    /// <summary>Revokes the authorizations and tokens the sign-in <paramref name="sid"/> got while impersonating <paramref name="userId"/>; returns how many.</summary>
    public static async Task<int> RevokeAsync(IServiceProvider services, string userId, string? sid, CancellationToken cancellationToken)
    {
        var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
        var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
        var revoked = 0;
        foreach (var authorization in await authorizations.FindBySubjectAsync(userId, cancellationToken).ToListAsync(cancellationToken))
        {
            if ((await authorizations.GetPropertiesAsync(authorization, cancellationToken)).TryGetValue(SessionProperty, out var value)
                && value.ValueKind == JsonValueKind.String && value.GetString() == (sid ?? string.Empty))
            {
                await tokens.RevokeByAuthorizationIdAsync((await authorizations.GetIdAsync(authorization, cancellationToken))!, cancellationToken);
                await authorizations.TryRevokeAsync(authorization, cancellationToken);
                revoked++;
            }
        }

        return revoked;
    }

    /// <summary>The account management pages answer 403 while someone is signed in as another user: he must not take over the user's credentials.</summary>
    private sealed class NotWhileImpersonatingFilter : IAsyncPageFilter
    {
        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

        public Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            if (IsImpersonating(context.HttpContext.User))
            {
                context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
                return Task.CompletedTask;
            }

            return next();
        }
    }
}
