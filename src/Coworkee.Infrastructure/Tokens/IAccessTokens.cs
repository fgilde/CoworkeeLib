namespace Coworkee.Infrastructure.Tokens;

/// <summary>Anonymous links that work for a short time, e.g. for a file viewer or a native download that sends no sign-in.</summary>
public interface IAccessTokens
{
    /// <summary>A new token for <paramref name="targetId"/>; the row is added to the unit of work, the caller saves it.</summary>
    string Issue(string purpose, Guid targetId, TimeSpan lifetime, bool singleUse);

    /// <summary>The grant, or null when the token is unknown, of another purpose, expired or (single use) taken already.</summary>
    Task<AccessTokenGrant?> RedeemAsync(string purpose, string token, CancellationToken cancellationToken);
}
