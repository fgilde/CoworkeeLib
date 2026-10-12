using Coworkee.Contracts.Identity;
using Coworkee.Domain;

namespace Coworkee.Identity.ApiKeys;

/// <summary>A personal key of a user for scripts and MCP clients; only the SHA-256 hash of its secret is stored.</summary>
[Realtime(ApiKeyPermissions.Manage)]
public sealed class ApiKey : AuditedAggregateRoot, IMultiTenant, IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>The first characters of the secret, so people can tell their keys apart.</summary>
    public required string Prefix { get; set; }

    public required string SecretHash { get; set; }

    public List<string> Scopes { get; set; } = [];

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public string? LastUsedIp { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? RevokedBy { get; set; }

    public IEnumerable<string> RealtimeTopics => [Contracts.Realtime.RealtimeTopics.User(UserId)];

    public ApiKeyDto ToDto() => new(Id, UserId, Name, Description, Prefix, Scopes, CreatedAt, ExpiresAt, LastUsedAt, LastUsedIp, RevokedAt);
}
