using System.Security.Claims;
using System.Text.Encodings.Web;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity.ApiKeys;

/// <summary>
/// Signs the owner of a valid key in. The key is looked up on every request, so revoking, expiry and deactivating the user apply at once;
/// the principal carries the key's scopes, which <see cref="Permissions.PermissionChecker"/> intersects with the user's current permissions.
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private static readonly TimeSpan UsageResolution = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var secret = Request.Headers[ApiKeyDefaults.Header].ToString() is { Length: > 0 } header
            ? header.Trim()
            : Request.Headers.Authorization.ToString() is var authorization && authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..].Trim() : string.Empty;
        if (!secret.StartsWith(ApiKeyDefaults.Prefix, StringComparison.Ordinal) || secret.Length > 200)
        {
            return AuthenticateResult.Fail("Not an API key.");
        }

        var hash = ApiKeySecret.Hash(secret);
        var db = Context.RequestServices.GetRequiredService<CoworkeeDbContext>();
        var now = clock.GetUtcNow();
        var keys = db.Set<ApiKey>().IgnoreQueryFilters([CoworkeeDbContext.TenantFilter]);
        var found = await (from key in keys
                           join user in db.Set<User>() on key.UserId equals user.Id
                           join tenant in db.Set<Tenant>() on key.TenantId equals tenant.Id
                           where key.SecretHash == hash
                           select new
                           {
                               key.Id, key.SecretHash, key.TenantId, key.UserId, key.Scopes, key.ExpiresAt, key.RevokedAt, key.LastUsedAt,
                               user.UserName, user.Email, user.IsActive, user.LockoutEnd, UserTenantId = user.TenantId, TenantActive = tenant.IsActive,
                           }).SingleOrDefaultAsync(Context.RequestAborted);
        if (found is null
            || !ApiKeySecret.Matches(hash, found.SecretHash)
            || found.RevokedAt is not null
            || found.ExpiresAt <= now
            || !found.IsActive
            || found.LockoutEnd > now
            || !found.TenantActive
            || found.UserTenantId != found.TenantId)
        {
            Logger.LogInformation("Rejected an API key starting with {Prefix}", secret[..Math.Min(secret.Length, 10)]);
            return AuthenticateResult.Fail("The API key is invalid, expired or revoked.");
        }

        if (found.LastUsedAt is null || found.LastUsedAt < now - UsageResolution)
        {
            var ip = Context.Connection.RemoteIpAddress?.ToString();
            await keys.Where(k => k.Id == found.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now).SetProperty(k => k.LastUsedIp, ip), Context.RequestAborted);
        }

        List<Claim> claims =
        [
            new("sub", found.UserId.ToString()),
            new("tenant", found.TenantId.ToString()),
            new("name", found.UserName ?? string.Empty),
            new(ApiKeyDefaults.IdClaim, found.Id.ToString()),
            .. found.Scopes.Select(s => new Claim(ApiKeyDefaults.ScopeClaim, s)),
        ];
        if (found.Email is { } email)
        {
            claims.Add(new Claim("email", email));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, ApiKeyDefaults.Scheme, "name", "role"));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, ApiKeyDefaults.Scheme));
    }
}
