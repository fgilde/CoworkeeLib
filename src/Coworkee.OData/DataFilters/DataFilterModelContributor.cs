using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData.DataFilters;

internal sealed class DataFilterModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataFilter>(filter =>
        {
            filter.ToTable("DataFilters", "cw");
            filter.Property(f => f.EntitySet).HasMaxLength(200);
            filter.Property(f => f.Layer).HasConversion<string>().HasMaxLength(20);
            filter.HasIndex(f => new { f.TenantId, f.EntitySet, f.Layer, f.LayerKey }).IsUnique();
        });
        modelBuilder.Entity<DataFilterLink>(link =>
        {
            link.ToTable("DataFilterLinks", "cw");
            link.Property(l => l.EntitySet).HasMaxLength(200);
            link.Property(l => l.Property).HasMaxLength(200);
            link.Property(l => l.TargetSet).HasMaxLength(200);
            link.HasIndex(l => l.EntitySet).IsUnique();
        });
    }
}
