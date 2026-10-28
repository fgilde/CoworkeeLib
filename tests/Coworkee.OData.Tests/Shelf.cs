using Coworkee.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using Nextended.Core.Facets;

namespace Coworkee.OData.Tests;

/// <summary>A row whose navigations lead to filtered rows: bins (a set with its own permission), their labels (a filter, no set) and lockers (a filter that is no Where).</summary>
public sealed class Shelf : AuditedEntity, IMultiTenant
{
    [ProvideFacet]
    public required string Name { get; set; }

    public List<Bin> Bins { get; set; } = [];

    [ProvideFacet(Label = "Featured", LabelPath = "it.Featured.Name")]
    public Guid? FeaturedId { get; set; }

    // a facet whose value is read through a navigation
    [ProvideFacet(Label = "Featured name", ValuePath = "it.Featured.Name")]
    public Bin? Featured { get; set; }

    public List<Locker> Lockers { get; set; } = [];

    public List<Peg> Pegs { get; set; } = [];

    // a range read through the filtered bins: smallest and largest come from the visible ones
    [ProvideFacet(Label = "Featured size", ValuePath = "Featured.Size", ValueType = typeof(int), Type = FacetType.Range)]
    public int Depth { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class Bin : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public int Size { get; set; }

    public Guid ShelfId { get; set; }

    public List<BinLabel> Labels { get; set; } = [];

    public Guid? BadgeId { get; set; }

    public Badge? Badge { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>A filtered set behind a filtered reference (Shelf.Featured.Badge); pages of one row.</summary>
[Page(PageSize = 1)]
public sealed class Badge : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>Expands its bin without $expand; searched by name, by its shelf's name (no rules) and by its bin's name (row filters).</summary>
public sealed class Tray : AuditedEntity, IMultiTenant, Nextended.Core.Contracts.ISearchable<Tray>
{
    public static System.Linq.Expressions.Expression<Func<Tray, string?>>[] SearchProperties => [t => t.Name, t => t.Shelf!.Name, t => t.Bin!.Name];

    public required string Name { get; set; }

    // a label read through a protected member of the target (Bin.Size needs SeeCategories)
    [ProvideFacet(Label = "Bin size", ValuePath = "BinId", LabelPath = "it.Bin.Size", ValueType = typeof(Guid))]
    public Guid? BinId { get; set; }

    // a key read through a filtered target: hidden targets count as no value
    [AutoExpand]
    [ProvideFacet(Label = "Bin", ValuePath = "it.Bin.Id")]
    public Bin? Bin { get; set; }

    public Guid? ShelfId { get; set; }

    // a target without rules: the facet through it stays
    [ProvideFacet(Label = "Shelf", ValuePath = "it.Shelf.Name")]
    public Shelf? Shelf { get; set; }

    public Guid? LockerId { get; set; }

    // a target whose filter is no Where: the facet through it is left out
    [ProvideFacet(Label = "Locker", ValuePath = "it.Locker.Name")]
    public Locker? Locker { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>A row whose filter reads the database, so it fails on the empty query the navigation scope reads filters from.</summary>
public sealed class Peg : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public Guid ShelfId { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class BinLabel : AuditedEntity, IMultiTenant
{
    public required string Text { get; set; }

    public Guid BinId { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class Locker : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public Guid ShelfId { get; set; }

    public Guid TenantId { get; set; }
}

internal sealed class HideSecretBins : IODataEntityFilter<Bin>
{
    public Task<IQueryable<Bin>> ApplyAsync(IQueryable<Bin> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(b => b.Name != "Secret"));
}

internal sealed class HideSecretLabels : IODataEntityFilter<BinLabel>
{
    public Task<IQueryable<BinLabel>> ApplyAsync(IQueryable<BinLabel> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(l => l.Text != "Secret"));
}

/// <summary>A filter that is no plain Where: it cannot apply inside another set's query.</summary>
internal sealed class FirstLockers : IODataEntityFilter<Locker>
{
    public Task<IQueryable<Locker>> ApplyAsync(IQueryable<Locker> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.OrderBy(l => l.Name).Take(10));
}

internal sealed class HideSecretBadges : IODataEntityFilter<Badge>
{
    public Task<IQueryable<Badge>> ApplyAsync(IQueryable<Badge> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(b => b.Name != "Secret"));
}

/// <summary>Asks the database first: works on the set's own query, throws on an in-memory one.</summary>
internal sealed class PegsWhenAny : IODataEntityFilter<Peg>
{
    public async Task<IQueryable<Peg>> ApplyAsync(IQueryable<Peg> query, CancellationToken cancellationToken) =>
        await query.AnyAsync(cancellationToken) ? query : query.Where(p => false);
}

/// <summary>A computed property of bins only users who see categories may read.</summary>
internal sealed class BinVolume : Coworkee.OData.Computed.IODataComputedPropertySource
{
    private static readonly System.Linq.Expressions.Expression<Func<Bin, int>> Volume = b => b.Size * 10;

    public IEnumerable<Coworkee.OData.Computed.ODataComputedProperty> Properties(ODataEntityRegistry registry) =>
        [new(typeof(Bin), "Volume", typeof(int), row => Coworkee.OData.Computed.ODataComputedProperty.Inline(Volume, row)) { Permission = ODataApp.SeeCategories }];
}

/// <summary>Facets through references read by key: a mapped readable foreign key, a shadow one, a protected one, and a registered key that is not EF's.</summary>
public sealed class Hook : AuditedEntity, IMultiTenant
{
    // a value of the row other than the facet's property, and protected (MainId needs SeeCategories)
    [ProvideFacet(Label = "Main id", ValuePath = "MainId", ValueType = typeof(Guid))]
    public required string Name { get; set; }

    [ProvideFacet(Label = "Spare", ValuePath = "it.Spare.Id")]
    public Bin? Spare { get; set; }

    public Guid? MainId { get; set; }

    [ProvideFacet(Label = "Main", ValuePath = "it.Main.Id")]
    public Bin? Main { get; set; }

    public Guid? BrandId { get; set; }

    [ProvideFacet(Label = "Brand", ValuePath = "it.Brand.Code")]
    public Brand? Brand { get; set; }

    // an owned type used twice (a type the navigation scope does not check itself) with a navigation into filtered bins
    [ProvideFacet(Label = "Front", ValuePath = "Front.BinId", LabelPath = "it.Front.Bin.Name", ValueType = typeof(Guid))]
    public Mount? Front { get; set; }

    [ProvideFacet(Label = "Back", ValuePath = "it.Back.Bin.Name")]
    public Mount? Back { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class Mount
{
    public Guid? BinId { get; set; }

    public Bin? Bin { get; set; }
}

/// <summary>A filtered set whose OData key (Code) is not its EF key (Id).</summary>
public sealed class Brand : AuditedEntity, IMultiTenant
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public Guid TenantId { get; set; }
}

internal sealed class HideSecretBrands : IODataEntityFilter<Brand>
{
    public Task<IQueryable<Brand>> ApplyAsync(IQueryable<Brand> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(b => b.Name != "Secret"));
}

/// <summary>A facet through a collection, which the facet builder cannot group by: refused when the model is built.</summary>
public sealed class Rack
{
    public Guid Id { get; set; }

    [ProvideFacet(Label = "Bins", ValuePath = "it.Bins.Id")]
    public List<Bin> Bins { get; set; } = [];
}
