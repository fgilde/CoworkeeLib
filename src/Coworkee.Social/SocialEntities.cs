using Coworkee.Contracts.Social;
using Coworkee.Domain;

namespace Coworkee.Social;

/// <summary>A comment on an entity; every add, edit, status change and delete raises <see cref="CommentChanged"/> after the commit.</summary>
public sealed class Comment : AggregateRoot, IMultiTenant, IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public Guid? ParentId { get; set; }

    public Guid AuthorId { get; set; }

    public required string Text { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

    public CommentStatus Status { get; set; }

    /// <summary>App values such as a reference number, stored as JSON.</summary>
    public Dictionary<string, string>? Properties { get; set; }

    public IEnumerable<string> RealtimeTopics => [SocialTopics.Comments(EntityType, EntityId)];

    internal void Changed(CommentAction action) => Raise(new CommentChanged(Id, EntityType, EntityId, Status, action));
}

public enum CommentAction
{
    Added,
    Edited,
    StatusChanged,
    Deleted,
}

/// <summary>
/// A comment was added, edited, got another status or was deleted. Handlers (<c>IDomainEventHandler&lt;CommentChanged&gt;</c>) run after the commit as the
/// user who did it, so they read the thread as it is now, e.g. to move a quotation to Request while one of its comments is Open.
/// </summary>
public sealed record CommentChanged(Guid CommentId, string EntityType, Guid EntityId, CommentStatus Status, CommentAction Action) : IDomainEvent;

/// <summary>A tag of the tag set of one entity type.</summary>
public sealed class Tag : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public required string Name { get; set; }
}

public sealed class EntityTag : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid TagId { get; set; }

    public Guid EntityId { get; set; }
}

public sealed class Rating : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public Guid UserId { get; set; }

    public int Stars { get; set; }
}

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
