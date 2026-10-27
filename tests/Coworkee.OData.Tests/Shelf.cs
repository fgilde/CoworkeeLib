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

/// <summary>Expands its bin without $expand.</summary>
public sealed class Tray : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public Guid? BinId { get; set; }

    [AutoExpand]
    public Bin? Bin { get; set; }

    public Guid? ShelfId { get; set; }

    public Shelf? Shelf { get; set; }

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
