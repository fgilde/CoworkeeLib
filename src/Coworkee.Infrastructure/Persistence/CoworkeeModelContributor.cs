using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Outbox;
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

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages", Schema);
            entity.Property(e => e.Type).HasMaxLength(1024);
            entity.HasIndex(e => new { e.ProcessedAt, e.OccurredAt });
        });
    }
}
