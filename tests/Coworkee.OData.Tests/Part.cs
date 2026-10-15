using Coworkee.Domain;

namespace Coworkee.OData.Tests;

public sealed class Part : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public int Quantity { get; set; }

    public Guid WidgetId { get; set; }

    public Widget? Widget { get; set; }

    public Guid TenantId { get; set; }
}
