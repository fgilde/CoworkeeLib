using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.ExtendedAttributes;

internal sealed class ExtendedAttributeModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ExtendedAttribute>(attribute =>
        {
            attribute.ToTable("ExtendedAttributes", "cw");
            attribute.Property(a => a.EntityType).HasMaxLength(100);
            attribute.Property(a => a.Key).HasMaxLength(100);
            attribute.Property(a => a.Group).HasMaxLength(100);
            attribute.Property(a => a.Description).HasMaxLength(500);
            attribute.Property(a => a.ExternalId).HasMaxLength(200);
            attribute.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
            attribute.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId, a.Key }).IsUnique();
        });
}
