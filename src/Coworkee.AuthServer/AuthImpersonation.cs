using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>Property of an OpenIddict application (<see cref="AuthClientOptions.AllowImpersonation"/>): someone signed in as another user may use it.</summary>
    public const string AllowedProperty = "coworkee_impersonation_allowed";

    /// <summary>Cookie claim while impersonating: the impersonator's own sign-in was persistent, so is the one "Back to my account" restores.</summary>
    public const string PersistentClaim = "coworkee_impersonator_persistent";

    private static readonly string[] CookieClaims =
        [ImpersonationClaims.Impersonator, ImpersonationClaims.ImpersonatorName, ImpersonationClaims.ImpersonatorStamp, ImpersonationClaims.Started, PersistentClaim];

    public static bool IsImpersonating(ClaimsPrincipal? principal) => principal?.FindFirst(ImpersonationClaims.Impersonator) is not null;

    public static void Conventions(RazorPagesOptions pages)
    {
        var filter = new NotWhileImpersonatingFilter();
        pages.Conventions.AddFolderApplicationModelConvention("/Account/Manage", model => model.Filters.Add(filter));
        pages.Conventions.AddPageApplicationModelConvention("/Account/ChangePasswordRequired", model => model.Filters.Add(filter));
        pages.Conventions.AddPageApplicationModelConvention("/Account/Consent", model => model.Filters.Add(filter));
    }

    // the cookie's sign-in is renewed when its security stamp is checked; the impersonation has to survive that
    public static Task KeepClaims(SecurityStampRefreshingPrincipalContext context)
    {
        var claims = context.CurrentPrincipal?.Claims.Where(c => CookieClaims.Contains(c.Type)).ToList() ?? [];
        if (claims.Count > 0 && context.NewPrincipal?.Identities.FirstOrDefault() is { } identity)
        {
            identity.AddClaims(claims.Select(c => new Claim(c.Type, c.Value)));
        }

        return Task.CompletedTask;
    }

    /// <summary>The cookie claims that start an impersonation of <paramref name="impersonator"/>.</summary>
    public static IEnumerable<Claim> StartClaims(User impersonator, bool persistent, DateTimeOffset now) =>
    [
        new(ImpersonationClaims.Impersonator, impersonator.Id.ToString()),
        new(ImpersonationClaims.ImpersonatorName, AuthEndpoints.DisplayName(impersonator)),
        new(ImpersonationClaims.ImpersonatorStamp, SessionStamp.Hash(impersonator.SecurityStamp)),
        new(ImpersonationClaims.Started, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        .. persistent ? [new Claim(PersistentClaim, "true")] : Array.Empty<Claim>(),
    ];

    /// <summary>
    /// The impersonator behind <paramref name="principal"/> (cookie or token) while he is active, not locked and has the security stamp he started with;
    /// otherwise null: he signed out everywhere, changed his password or was locked, and the impersonation ends.
    /// </summary>
    public static async Task<User?> ImpersonatorAsync(IServiceProvider services, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ImpersonationClaims.Impersonator), out var id))
        {
            return null;
        }

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var user = await services.GetRequiredService<CoworkeeDbContext>().Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.IsActive, cancellationToken);
        return user is not null && !(user.LockoutEnabled && user.LockoutEnd > now)
               && principal.FindFirstValue(ImpersonationClaims.ImpersonatorStamp) == SessionStamp.Hash(user.SecurityStamp) ? user : null;
    }

    /// <summary>
    /// Whether the impersonation of <paramref name="target"/> behind <paramref name="principal"/> goes on at this authorization or refresh: the impersonator
    /// is still valid (<see cref="ImpersonatorAsync"/>), within <see cref="AuthServerOptions.MaxImpersonationDuration"/>, and the rules still allow it.
    /// </summary>
    public static async Task<bool> MayContinueAsync(IServiceProvider services, ClaimsPrincipal principal, User target, CancellationToken cancellationToken)
    {
        var maximum = services.GetRequiredService<IOptions<AuthServerOptions>>().Value.MaxImpersonationDuration;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        return await ImpersonatorAsync(services, principal, cancellationToken) is { } impersonator
               && long.TryParse(principal.FindFirstValue(ImpersonationClaims.Started), NumberStyles.None, CultureInfo.InvariantCulture, out var started)
               && now - DateTimeOffset.FromUnixTimeSeconds(started) <= maximum
               && await services.GetRequiredService<ImpersonationRules>().CheckAsync(impersonator.Id, null, target.Id, target.TenantId, cancellationToken) is null;
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

    /// <summary>
    /// Revokes the authorizations and tokens the sign-in <paramref name="sid"/> got while impersonating <paramref name="userId"/> and announces the end
    /// (<see cref="ImpersonationEnded"/> through the outbox) to the clients of that sign-in; returns how many authorizations it revoked.
    /// </summary>
    public static async Task<int> RevokeAsync(IServiceProvider services, ClaimsPrincipal principal, string userId, string? sid, CancellationToken cancellationToken)
    {
        await AnnounceEndAsync(services, principal, userId, sid, cancellationToken);
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

    // a scope of its own: the caller's unit of work may hold changes that are not meant to be saved here
    private static async Task AnnounceEndAsync(IServiceProvider services, ClaimsPrincipal principal, string userId, string? sid, CancellationToken cancellationToken)
    {
        // without a session id there is no topic only the impersonating session follows
        if (sid is not { Length: > 0 } || !Guid.TryParse(principal.FindFirstValue(ImpersonationClaims.Impersonator), out var impersonator) || !Guid.TryParse(userId, out var target))
        {
            return;
        }

        await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        db.Add(new ImpersonationEnded(target, impersonator, sid).ToOutbox(services.GetRequiredService<TimeProvider>().GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The account management and consent pages answer 403 while someone is signed in as another user: he must not take over the user's credentials or consent for him.</summary>
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
