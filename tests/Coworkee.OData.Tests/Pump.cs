using Coworkee.Domain;
using Nextended.Core.Facets;

namespace Coworkee.OData.Tests;

/// <summary>Facets of every kind the one read counts (text, enum, yes/no, number, ranges, a computed state) and one through a navigation (its maker).</summary>
public sealed class Pump : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    [ProvideFacet(Label = "Color")]
    public string? Color { get; set; }

    [ProvideFacet(Label = "Kind")]
    public WidgetKind Kind { get; set; }

    [ProvideFacet(Label = "Active")]
    public bool Active { get; set; }

    [ProvideFacet(Label = "Stages")]
    public int Stages { get; set; }

    [ProvideFacet(Label = "Price", Type = FacetType.Range)]
    public decimal Price { get; set; }

    [ProvideFacet(Label = "Made", Type = FacetType.DateRange)]
    public DateTimeOffset MadeAt { get; set; }

    [ProvideFacet(Label = "Checked", Type = FacetType.DateRange)]
    public DateTime? CheckedAt { get; set; }

    // "it." because the label reads a navigation, the widgets' row filter hides the secret ones
    [ProvideFacet(Label = "Maker", ValuePath = "MakerId", LabelPath = "it.Maker.Name", ValueType = typeof(Guid))]
    public Guid? MakerId { get; set; }

    public Widget? Maker { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>A computed state with a facet: "Large" from a price of 50.</summary>
internal sealed class PumpSize : Coworkee.OData.Computed.IODataComputedPropertySource
{
    private static readonly System.Linq.Expressions.Expression<Func<Pump, string>> Size = p => p.Price >= 50 ? "Large" : "Small";

    public IEnumerable<Coworkee.OData.Computed.ODataComputedProperty> Properties(ODataEntityRegistry registry) =>
    [
        new(typeof(Pump), "Size", typeof(string), row => Coworkee.OData.Computed.ODataComputedProperty.Inline(Size, row))
        {
            Facet = new("Size", [new("Large", "Large", "Size eq 'Large'", "#c62828"), new("Small", "Small", "Size eq 'Small'")]),
        },
    ];
}
