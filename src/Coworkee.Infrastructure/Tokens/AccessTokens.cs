using System.Security.Cryptography;
using System.Text;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Infrastructure.Tokens;

internal sealed class AccessTokens(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock, RetentionCleansAccessTokens? retention = null) : IAccessTokens
{
    public const int PurposeLength = 50;

    public string Issue(string purpose, Guid targetId, TimeSpan lifetime, bool singleUse)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        db.Add(new AccessToken
        {
            Hash = Hash(token),
            Purpose = purpose,
            TargetId = targetId,
            TenantId = currentUser.TenantId,
            UserId = currentUser.UserId,
            ExpiresAt = clock.GetUtcNow().Add(lifetime),
            SingleUse = singleUse,
        });
        return token;
    }

    public async Task<AccessTokenGrant?> RedeemAsync(string purpose, string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 100)
        {
            return null;
        }

        var hash = Hash(token);
        var now = clock.GetUtcNow();
        var row = await db.Set<AccessToken>().AsNoTracking().SingleOrDefaultAsync(t => t.Hash == hash && t.Purpose == purpose, cancellationToken);
        if (row is null || row.ExpiresAt <= now || row.UsedAt is not null)
        {
            return null;
        }

        // one conditional update takes a single use token, so two parallel requests cannot both win
        if (row.SingleUse && await db.Set<AccessToken>().Where(t => t.Id == row.Id && t.UsedAt == null)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.UsedAt, now), cancellationToken) != 1)
        {
            return null;
        }

        if (retention is null)
        {
            // ponytail: without the Retention module tokens expired a day ago go when one is redeemed
            var old = now.AddDays(-1);
            await db.Set<AccessToken>().Where(t => t.ExpiresAt < old).ExecuteDeleteAsync(cancellationToken);
        }

        return new AccessTokenGrant(row.TargetId, row.TenantId, row.UserId);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
