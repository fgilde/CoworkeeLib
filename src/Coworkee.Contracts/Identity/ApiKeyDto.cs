namespace Coworkee.Contracts.Identity;

public sealed record ApiKeyDto(
    Guid Id, Guid UserId, string Name, string? Description, string Prefix, IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? LastUsedAt, string? LastUsedIp, DateTimeOffset? RevokedAt);
