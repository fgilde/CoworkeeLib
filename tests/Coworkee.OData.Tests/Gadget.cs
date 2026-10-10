using Coworkee.Domain;
using Nextended.Core.Facets;

namespace Coworkee.OData.Tests;

[ClientEntity]
public sealed class Gadget : AuditedEntity, IMultiTenant, ISoftDelete
{
    public required string Name { get; set; }

    [ProvideFacet(Label = "Category")]
    public required string Category { get; set; }

    public decimal Price { get; set; }

    public string? SerialCode { get; set; }

    public Guid TenantId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}
