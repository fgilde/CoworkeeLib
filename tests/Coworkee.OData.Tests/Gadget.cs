using Coworkee.Domain;
using Nextended.Core.Facets;

namespace Coworkee.OData.Tests;

public sealed class Gadget : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    [ProvideFacet(Label = "Category")]
    public required string Category { get; set; }

    public decimal Price { get; set; }

    public Guid TenantId { get; set; }
}
