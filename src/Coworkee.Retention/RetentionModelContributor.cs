using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Retention;

internal sealed class RetentionModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<RetentionRun>(run =>
        {
            run.ToTable("RetentionRuns", "cw");
            run.HasKey(r => r.Key);
            run.Property(r => r.Key).HasMaxLength(100);
            run.Property(r => r.LastError).HasMaxLength(4000);
        });
}
