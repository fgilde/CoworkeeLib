using Coworkee.Domain;

namespace Coworkee.Identity.Domain;

public sealed class Tenant : AuditedAggregateRoot
{
    public required string Name { get; set; }

    public required string Identifier { get; set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "Active")]
    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; }

    public bool AcceptsRegistrations { get; set; }
}
