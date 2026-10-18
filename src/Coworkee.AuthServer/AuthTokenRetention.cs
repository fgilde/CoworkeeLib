using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

/// <summary>What OpenIddict's prune removes, created before the day: tokens that are not valid or inactive, belong to an invalid authorization or expired, and authorizations without tokens that are invalid or ad hoc. The count leaves out authorizations that only lose their tokens in the same prune.</summary>
internal sealed class AuthTokenRetention(CoworkeeDbContext db, IOpenIddictTokenManager tokens, IOpenIddictAuthorizationManager authorizations)
    : IRetentionTarget
{
    public string Key => "auth-tokens";

    public string Title => "Expired sign-in tokens";

    public string? Description => "Revoked, used and expired tokens and authorizations of the auth server; valid ones stay.";

    public int? DefaultDays => null;

    // the conditions of OpenIddict 7.7's EF Core stores; their prune deletes 1000 rows per statement, tokens before authorizations
    public async Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        var date = before.UtcDateTime;
        var now = DateTime.UtcNow;
        var expired = await db.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().LongCountAsync(t => t.CreationDate < date
            && ((t.Status != Statuses.Inactive && t.Status != Statuses.Valid) || (t.Authorization != null && t.Authorization.Status != Statuses.Valid) || t.ExpirationDate < now),
            cancellationToken);
        return expired + await db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().LongCountAsync(a => a.CreationDate < date
            && (a.Status != Statuses.Valid || a.Type == AuthorizationTypes.AdHoc) && !a.Tokens.Any(), cancellationToken);
    }

    public async Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        await tokens.PruneAsync(before, cancellationToken) + await authorizations.PruneAsync(before, cancellationToken);
}
