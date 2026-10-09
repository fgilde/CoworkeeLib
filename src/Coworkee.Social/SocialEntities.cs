using Coworkee.Contracts.Social;
using Coworkee.Domain;

namespace Coworkee.Social;

public sealed class Comment : Entity, IMultiTenant, IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public Guid? ParentId { get; set; }

    public Guid AuthorId { get; set; }

    public required string Text { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

    public IEnumerable<string> RealtimeTopics => [SocialTopics.Comments(EntityType, EntityId)];
}

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
