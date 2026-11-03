namespace Coworkee.Contracts.Identity;

public sealed record CreateApiKeyRequest(string Name, string? Description, DateTimeOffset ExpiresAt, IReadOnlyList<string> Scopes);
