using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

/// <summary>Tokens and authorizations OpenIddict's prune removes: revoked, redeemed or expired ones and ad-hoc authorizations without tokens, created before the day.</summary>
internal sealed class AuthTokenRetention(CoworkeeDbContext db, IOpenIddictTokenManager tokens, IOpenIddictAuthorizationManager authorizations, TimeProvider clock)
    : IRetentionTarget
{
    public string Key => "auth-tokens";

    public string Title => "Expired sign-in tokens";

    public string? Description => "Revoked, used and expired tokens and authorizations of the auth server; valid ones stay.";

    public int? DefaultDays => null;

    public async Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        var date = before.UtcDateTime;
        var now = clock.GetUtcNow().UtcDateTime;
        var expired = await db.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().LongCountAsync(t => t.CreationDate < date
            && ((t.Status != Statuses.Inactive && t.Status != Statuses.Valid) || (t.Authorization != null && t.Authorization.Status != Statuses.Valid) || t.ExpirationDate < now),
            cancellationToken);
        return expired + await db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().LongCountAsync(a => a.CreationDate < date
            && (a.Status != Statuses.Valid || (a.Type == AuthorizationTypes.AdHoc && !a.Tokens.Any())), cancellationToken);
    }

    public async Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        await tokens.PruneAsync(before, cancellationToken) + await authorizations.PruneAsync(before, cancellationToken);
}
