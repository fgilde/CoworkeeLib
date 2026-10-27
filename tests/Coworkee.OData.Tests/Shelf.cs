using Coworkee.Domain;

namespace Coworkee.OData.Tests;

/// <summary>A row whose navigations lead to filtered rows: bins (a set with its own permission), their labels (a filter, no set) and lockers (a filter that is no Where).</summary>
public sealed class Shelf : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public List<Bin> Bins { get; set; } = [];

    public Guid? FeaturedId { get; set; }

    public Bin? Featured { get; set; }

    public List<Locker> Lockers { get; set; } = [];

    public Guid TenantId { get; set; }
}

public sealed class Bin : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public int Size { get; set; }

    public Guid ShelfId { get; set; }

    public List<BinLabel> Labels { get; set; } = [];

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
