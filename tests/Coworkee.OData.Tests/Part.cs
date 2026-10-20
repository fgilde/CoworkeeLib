using Coworkee.Domain;
using Nextended.Core.Facets;

namespace Coworkee.OData.Tests;

public sealed class Part : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public int Quantity { get; set; }

    public Guid WidgetId { get; set; }

    public Widget? Widget { get; set; }

    // "Parent" alone is a Dynamic LINQ keyword; "it." keeps the facet query working
    [ProvideFacet(Label = "Parent", ValuePath = "ParentId", LabelPath = "it.Parent.Name", ValueType = typeof(Guid))]
    public Guid? ParentId { get; set; }

    public Part? Parent { get; set; }

    public Guid TenantId { get; set; }
}
