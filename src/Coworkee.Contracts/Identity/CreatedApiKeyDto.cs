namespace Coworkee.Contracts.Identity;

/// <summary>The only time the secret leaves the server: it is stored as a hash.</summary>
public sealed record CreatedApiKeyDto(ApiKeyDto Key, string Secret);
