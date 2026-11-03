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
