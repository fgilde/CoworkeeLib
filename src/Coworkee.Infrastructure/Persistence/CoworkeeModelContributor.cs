using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Infrastructure.Persistence;

internal sealed class CoworkeeModelContributor : IModelContributor
{
    public const string Schema = "cw";

    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("AuditEntries", Schema);
            entity.Property(e => e.EntityType).HasMaxLength(200);
            entity.Property(e => e.EntityId).HasMaxLength(200);
            entity.Property(e => e.CorrelationId).HasMaxLength(64);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.OccurredAt);
            entity.OwnsMany(e => e.Changes, changes => changes.ToJson());
        });
    }
}
