using Coworkee.Domain;

namespace Coworkee.Social;

public sealed class EntityTag : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public Guid TagId { get; set; }

    public Guid EntityId { get; set; }
}
