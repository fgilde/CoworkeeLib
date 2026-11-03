using Coworkee.Domain;

namespace Coworkee.Social;

/// <summary>A tag of the tag set of one entity type.</summary>
public sealed class Tag : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public required string Name { get; set; }
}
