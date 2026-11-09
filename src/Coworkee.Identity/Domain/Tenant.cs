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

    /// <summary>Runs the seed contributors of the api for this tenant once the save commits (<see cref="Setup.TenantSeedRequested"/>).</summary>
    public void RequestSeed() => Raise(new Setup.TenantSeedRequested(Id));
}
