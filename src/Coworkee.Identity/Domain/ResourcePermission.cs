using Coworkee.Contracts.Identity;
using Coworkee.Domain;

namespace Coworkee.Identity.Domain;

public sealed class ResourcePermission : AuditedEntity, IMultiTenant
{
    public required string ResourceType { get; set; }

    public Guid ResourceId { get; set; }

    public PrincipalType PrincipalType { get; set; }

    public Guid PrincipalId { get; set; }

    public Guid RoleId { get; set; }

    public Guid TenantId { get; set; }
}
