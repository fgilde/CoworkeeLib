using System.Security.Cryptography;
using System.Text;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Infrastructure.Tokens;

/// <summary>A short-lived token that opens one thing without sign-in; only the hash of the token is stored. Expired ones go with the retention target access-tokens, without the Retention module when another token is redeemed.</summary>
[NotAudited]
public sealed class AccessToken : Entity
{
    public required string Hash { get; set; }

    public required string Purpose { get; set; }

    public Guid TargetId { get; set; }

    public Guid? TenantId { get; set; }

    public Guid? UserId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public bool SingleUse { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>What a redeemed token opens, with the organisation and user that issued it.</summary>
public sealed record AccessTokenGrant(Guid TargetId, Guid? TenantId, Guid? UserId);

/// <summary>Anonymous links that work for a short time, e.g. for a file viewer or a native download that sends no sign-in.</summary>
public interface IAccessTokens
{
    /// <summary>A new token for <paramref name="targetId"/>; the row is added to the unit of work, the caller saves it.</summary>
    string Issue(string purpose, Guid targetId, TimeSpan lifetime, bool singleUse);

    /// <summary>The grant, or null when the token is unknown, of another purpose, expired or (single use) taken already.</summary>
    Task<AccessTokenGrant?> RedeemAsync(string purpose, string token, CancellationToken cancellationToken);
}

/// <summary>Registered by the Retention module, whose target access-tokens deletes expired tokens; without it tokens clean up after themselves.</summary>
public sealed class RetentionCleansAccessTokens;

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
