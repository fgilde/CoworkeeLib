using Coworkee.Domain;

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
