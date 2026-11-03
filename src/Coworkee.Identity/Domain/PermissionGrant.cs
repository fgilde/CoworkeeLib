using Coworkee.Contracts.Identity;
using Coworkee.Domain;

namespace Coworkee.Identity.Domain;

public sealed class PermissionGrant : AuditedEntity
{
    public Guid? TenantId { get; set; }

    public required string Name { get; set; }

    public PermissionProviderType ProviderType { get; set; }

    public Guid ProviderKey { get; set; }
}
