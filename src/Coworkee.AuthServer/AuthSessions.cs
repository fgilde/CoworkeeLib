using System.Security.Claims;
using System.Text.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Sessions;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

/// <summary>
/// The sessions of the auth server's sign-ins (<see cref="UserSession"/>): a new sign-in starts one and applies the session limit; the cookie, the
/// authorization and the token refresh stop as soon as it ended; its authorizations carry its id, so ending it revokes exactly its tokens.
/// </summary>
internal static class AuthSessions
{
    /// <summary>Property of the authorizations the auth server creates: the session id (<c>sid</c>) of the sign-in.</summary>
    public const string Property = "coworkee_sid";

    private const string RefusedItem = "coworkee:session-refused";

    // the last activity is written at most this often: every token refresh would be a write otherwise
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    /// <summary>The sign-in of this request went beyond the session limit in the mode "refuse"; the browser got no sign-in.</summary>
    public static bool Refused(HttpContext context) => context.Items.ContainsKey(RefusedItem);

    /// <summary>The cookie's sign-in: a new one (without a session id) starts a session; a renewal keeps its own.</summary>
    public static async Task StartAsync(CookieSigningInContext context)
    {
        var sid = Guid.NewGuid();
        if (!context.Properties.Items.TryAdd(OwnPassword.SessionItem, sid.ToString("N"))
            || !Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return;
        }

        var http = context.HttpContext;
        var now = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var impersonator = Guid.TryParse(context.Principal!.FindFirstValue(ImpersonationClaims.Impersonator), out var by) ? by : (Guid?)null;
        // the sign-in this browser had before is over; "Log in as" keeps the impersonator's own, its tokens stay valid for "Back to my account"
        var previous = impersonator is null && Guid.TryParse(AuthEndpoints.SessionOf(await http.AuthenticateAsync(IdentityConstants.ApplicationScheme)), out var id)
            ? id : (Guid?)null;
        await using var scope = http.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        Guid tenantId;
        int? own;
        using (CurrentUserScope.Begin(new ImpersonatedUser(null, null)))
        {
            (tenantId, own) = await db.Set<User>().AsNoTracking().Where(u => u.Id == userId).Select(u => new ValueTuple<Guid, int?>(u.TenantId, u.MaxSessions)).SingleAsync(http.RequestAborted);
        }

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        var sessions = db.Set<UserSession>().IgnoreQueryFilters([CoworkeeDbContext.TenantFilter]);
        if (previous is { } replaced && await sessions.SingleOrDefaultAsync(s => s.Id == replaced, http.RequestAborted) is { } before)
        {
            before.End(now, SessionEndReasons.Replaced, null, quiet: true);
        }

        if (impersonator is null && !await MakeRoomAsync(scope.ServiceProvider, sessions, userId, own, now, http.RequestAborted))
        {
            // no cookie for this sign-in: it is written as expired; the page tells the user
            http.Items[RefusedItem] = true;
            context.Properties.IsPersistent = true;
            context.Properties.AllowRefresh = false;
            context.Properties.ExpiresUtc = DateTimeOffset.UnixEpoch;
            await db.SaveChangesAsync(http.RequestAborted);
            return;
        }

        var userAgent = http.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent[..Math.Min(agent.Length, 512)] : null;
        var (device, browser) = UserAgents.Describe(userAgent);
        db.Add(new UserSession(sid)
        {
            TenantId = tenantId,
            UserId = userId,
            UserAgent = userAgent,
            Device = device,
            Browser = browser,
            IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            ImpersonatorId = impersonator,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + context.Options.ExpireTimeSpan,
        });
        await db.SaveChangesAsync(http.RequestAborted);
    }

    /// <summary>
    /// Whether one more session fits the user's limit (his own, else the organisation's; 0 is unlimited); in the mode "end the oldest" it ends as many of the
    /// oldest as needed. Sessions of someone signed in as the user neither count nor end.
    /// </summary>
    private static async Task<bool> MakeRoomAsync(IServiceProvider services, IQueryable<UserSession> sessions, Guid userId, int? own, DateTimeOffset now, CancellationToken ct)
    {
        var settings = services.GetRequiredService<ISettingProvider>();
        var limit = own ?? await settings.GetAsync<int>(AuthSettings.MaxSessionsPerUser, ct);
        if (limit <= 0)
        {
            return true;
        }

        // the session this browser replaces is ended in the change tracker already
        var active = (await sessions.Where(UserSession.IsActive(now)).Where(s => s.UserId == userId && s.ImpersonatorId == null).OrderBy(s => s.CreatedAt).ToListAsync(ct))
            .Where(s => s.EndedAt is null).ToList();
        if (active.Count < limit)
        {
            return true;
        }

        if (await settings.GetAsync(AuthSettings.SessionLimitMode, ct) == SessionLimitModes.Refuse)
        {
            return false;
        }

        foreach (var oldest in active.Take(active.Count - limit + 1))
        {
            oldest.End(now, SessionEndReasons.Limit, null);
        }

        return true;
    }

    /// <summary>The cookie of an ended session no longer signs in; checked on every request of the auth server that carries one.</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var sid = context.Properties.Items.TryGetValue(OwnPassword.SessionItem, out var item) ? item : null;
        if (context.Principal is null)
        {
            return;
        }

        if (await IsEndedAsync(context.HttpContext.RequestServices.GetRequiredService<CoworkeeDbContext>(), sid, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }

        // an account page that changes the security stamp signs this browser in again: its session goes on (SessionChangeInterceptor)
        context.HttpContext.Items[SessionStamp.RequestSessionItem] = sid;
    }

    public static Task<bool> IsEndedAsync(CoworkeeDbContext db, string? sid, CancellationToken ct) =>
        Guid.TryParse(sid, out var id) ? db.Set<UserSession>().IgnoreQueryFilters([CoworkeeDbContext.TenantFilter]).AnyAsync(s => s.Id == id && s.EndedAt != null, ct)
            : Task.FromResult(false);

    /// <summary>Notes activity of the session (an authorization or token refresh): last seen, the expiry moved on, the first app it signed in to.</summary>
    public static async Task TouchAsync(IServiceProvider services, string? sid, string? clientId, CancellationToken ct)
    {
        if (!Guid.TryParse(sid, out var id))
        {
            return;
        }

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var expires = now + services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme).ExpireTimeSpan;
        var stale = now - TouchInterval;
        // ponytail: a bulk update without realtime message; open lists show the activity at their next load
        await services.GetRequiredService<CoworkeeDbContext>().Set<UserSession>().IgnoreQueryFilters([CoworkeeDbContext.TenantFilter])
            .Where(s => s.Id == id && s.EndedAt == null && (s.LastSeenAt < stale || (clientId != null && s.ClientId == null)))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, now).SetProperty(s => s.ExpiresAt, expires).SetProperty(s => s.ClientId, s => s.ClientId ?? clientId), ct);
    }

    /// <summary>The user signed out in this browser: the session ends with its tokens, without a message to the browser that is leaving anyway.</summary>
    public static async Task EndAsync(IServiceProvider services, string? sid, CancellationToken ct)
    {
        if (!Guid.TryParse(sid, out var id))
        {
            return;
        }

        await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        if (await db.Set<UserSession>().IgnoreQueryFilters([CoworkeeDbContext.TenantFilter]).SingleOrDefaultAsync(s => s.Id == id, ct) is { } session)
        {
            session.End(services.GetRequiredService<TimeProvider>().GetUtcNow(), SessionEndReasons.LoggedOut, null, quiet: true);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>The authorization of a sign-in's tokens, named after its session; while impersonating also after the impersonation (<see cref="AuthImpersonation.RevokeAsync"/>).</summary>
    public static async Task<string> CreateAuthorizationAsync(
        IServiceProvider services, OpenIddictRequest request, ClaimsPrincipal principal, string? sid, DateTimeOffset now, bool impersonating)
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
        descriptor.Properties[Property] = JsonSerializer.SerializeToElement(sid ?? string.Empty);
        if (impersonating)
        {
            descriptor.Properties[AuthImpersonation.SessionProperty] = JsonSerializer.SerializeToElement(sid ?? string.Empty);
        }

        return (await authorizations.GetIdAsync(await authorizations.CreateAsync(descriptor)))!;
    }

    /// <summary>Whether the authorization's properties name the session <paramref name="sid"/> under <paramref name="property"/>.</summary>
    public static bool Names(IReadOnlyDictionary<string, JsonElement> properties, string property, string sid) =>
        properties.TryGetValue(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == sid;
}
