using Coworkee.Domain;

namespace Coworkee.Social;

public sealed class Rating : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public Guid UserId { get; set; }

    public int Stars { get; set; }
}
