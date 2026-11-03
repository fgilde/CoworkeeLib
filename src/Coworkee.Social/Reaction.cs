using Coworkee.Contracts.Social;
using Coworkee.Domain;

namespace Coworkee.Social;

/// <summary>One emoji of one user on an entity (or on a comment, entity type <see cref="SocialReactions.Comment"/>).</summary>
public sealed class Reaction : Entity, IMultiTenant, IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public Guid UserId { get; set; }

    public required string Emoji { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Not stored: the comments topic of the commented entity, so open threads follow reactions on their comments.</summary>
    internal string? ThreadTopic { get; set; }

    public IEnumerable<string> RealtimeTopics => ThreadTopic is null ? [SocialTopics.Reactions(EntityType, EntityId)] : [SocialTopics.Reactions(EntityType, EntityId), ThreadTopic];
}
