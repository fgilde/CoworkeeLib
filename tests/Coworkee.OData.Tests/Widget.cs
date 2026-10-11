using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Coworkee.Contracts.Data;
using Coworkee.Domain;
using Nextended.Core.Facets;

namespace Coworkee.OData.Tests;

/// <summary>A row with a facet of every kind: enum, flags, yes/no, number range, date range and a single choice.</summary>
public sealed class Widget : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    [ProvideFacet(Label = "Kind", Order = 0)]
    [FacetBehavior(AllowExclude = true)]
    public WidgetKind Kind { get; set; }

    [ProvideFacet(Label = "Features", Order = 1)]
    [FacetBehavior(Combine = FacetCombine.All, Switchable = true)]
    public WidgetFeatures Features { get; set; }

    [ProvideFacet(Label = "Active", Order = 2)]
    public bool Active { get; set; }

    [ProvideFacet(Label = "Price", Type = FacetType.Range, Order = 3)]
    [FacetBehavior(Buckets = 4)]
    public decimal Price { get; set; }

    [ProvideFacet(Label = "Made", Type = FacetType.DateRange, Order = 4)]
    public DateTimeOffset MadeAt { get; set; }

    [ProvideFacet(Label = "Color", MultiSelect = false, Order = 5)]
    public required string Color { get; set; }

    public Guid TenantId { get; set; }
}

public enum WidgetKind
{
    [Display(Name = "Claw hammer")]
    Hammer,
    Wrench,
    [Description("Hand saw")]
    Saw,
    PowerDrill,
}

[Flags]
public enum WidgetFeatures
{
    None = 0,
    Express = 1,
    Gift = 2,
    [FacetColor("#00838f")]
    Insured = 4,
}
