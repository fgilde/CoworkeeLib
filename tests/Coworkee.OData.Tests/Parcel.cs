using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData.Tests;

/// <summary>A row of a database the app does not own: no tenant, no audit columns, only the dates its owner keeps.</summary>
public sealed class Parcel
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public string Owner { get; set; } = "";

    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? DateUpdatedUtc { get; set; }
}

/// <summary>A row whose model only EF knows right: a value the database computes (no public setter) and pallets keyed by an inherited <c>Id</c>.</summary>
public sealed class Consignment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public int Weight { get; set; }

    /// <summary>Computed by the database; C# only reads it.</summary>
    public int DoubleWeight { get; private set; }

    public List<Pallet> Pallets { get; set; } = [];

    /// <summary>Read only in C#, hidden by the set.</summary>
    public string? Memo { get; private set; } = "internal memo";
}

public abstract class ExternalRow
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>The source system's number <c>PalletId</c> next to the inherited key: the OData convention sees two key candidates.</summary>
public sealed class Pallet : ExternalRow
{
    public int PalletId { get; set; }

    public required string Label { get; set; }

    public Guid ConsignmentId { get; set; }

    /// <summary>Read only in C#; pallets have no set that could hide it.</summary>
    public string Secret { get; private set; } = "pallet secret";
}

public sealed class ParcelDbContext(DbContextOptions<ParcelDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Parcel>(parcel => parcel.ToTable("ExternalParcels"));
        modelBuilder.Entity<Consignment>(consignment =>
        {
            consignment.ToTable("ExternalConsignments");
            consignment.Property(c => c.DoubleWeight).HasComputedColumnSql("\"Weight\" * 2", stored: true);
            consignment.HasMany(c => c.Pallets).WithOne().HasForeignKey(p => p.ConsignmentId);
        });
        modelBuilder.Entity<Pallet>(pallet => pallet.ToTable("ExternalPallets"));
    }
}

internal sealed class HideSecretParcels : IODataEntityFilter<Parcel>
{
    public Task<IQueryable<Parcel>> ApplyAsync(IQueryable<Parcel> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(p => p.Owner != "Secret"));
}

/// <summary>A value computed from the columns of an external row: no ComputedQueryRoot, so it needs no interceptor of the context.</summary>
internal sealed class ParcelLabel : Coworkee.OData.Computed.IODataComputedPropertySource
{
    private static readonly System.Linq.Expressions.Expression<Func<Parcel, string>> Label = p => p.Name + " (" + p.Owner + ")";

    public IEnumerable<Coworkee.OData.Computed.ODataComputedProperty> Properties(ODataEntityRegistry registry) =>
        [new(typeof(Parcel), "Label", typeof(string), row => Coworkee.OData.Computed.ODataComputedProperty.Inline(Label, row))];
}

/// <summary>Writes into both databases and leaves saving to the pipeline.</summary>
public sealed record ShipParcelCommand(string Name) : ICommand<Result<Guid>>;

internal sealed class ShipParcelHandler(ParcelDbContext parcels, GadgetDbContext gadgets, ICurrentUser user) : IHandler<ShipParcelCommand, Result<Guid>>
{
    public Task<Result<Guid>> HandleAsync(ShipParcelCommand request, CancellationToken cancellationToken)
    {
        var parcel = new Parcel { Name = request.Name };
        parcels.Add(parcel);
        gadgets.Add(new Gadget { Name = request.Name, Category = "Shipped", TenantId = user.TenantId!.Value });
        return Task.FromResult<Result<Guid>>(parcel.Id);
    }
}
