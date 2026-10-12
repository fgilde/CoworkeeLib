namespace Coworkee.Contracts.Identity;

public static class ApiKeyPermissions
{
    public const string GroupName = "ApiKeys";

    /// <summary>Create and revoke own keys, limited to the permissions the user has.</summary>
    public const string Create = "ApiKeys.Create";

    /// <summary>See and revoke every key of the organisation.</summary>
    public const string Manage = "ApiKeys.Manage";
}

/// <summary>How API keys travel: <c>Authorization: Bearer cwk_…</c> or the <see cref="Header"/> header.</summary>
public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";

    public const string Prefix = "cwk_";

    public const string Header = "X-Api-Key";

    public const string IdClaim = "coworkee:api_key";

    public const string ScopeClaim = "coworkee:scope";

    public const int MaxLifetimeDays = 366;
}

public sealed record CreateApiKeyRequest(string Name, string? Description, DateTimeOffset ExpiresAt, IReadOnlyList<string> Scopes);

public sealed record ApiKeyDto(
    Guid Id, Guid UserId, string Name, string? Description, string Prefix, IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? LastUsedAt, string? LastUsedIp, DateTimeOffset? RevokedAt);

/// <summary>The only time the secret leaves the server: it is stored as a hash.</summary>
public sealed record CreatedApiKeyDto(ApiKeyDto Key, string Secret);
