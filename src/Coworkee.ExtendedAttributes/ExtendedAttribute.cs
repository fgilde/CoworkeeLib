using Coworkee.Contracts.ExtendedAttributes;
using Coworkee.Domain;

namespace Coworkee.ExtendedAttributes;

public sealed class ExtendedAttribute : AuditedEntity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public required string Key { get; set; }

    public ExtendedAttributeType Type { get; set; }

    public string? Text { get; set; }

    public decimal? Decimal { get; set; }

    public DateTimeOffset? DateTime { get; set; }

    public string? Json { get; set; }

    public string? Group { get; set; }

    public string? Description { get; set; }

    public string? ExternalId { get; set; }

    public bool IsActive { get; set; } = true;
}
