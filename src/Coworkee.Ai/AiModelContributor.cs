using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Ai;

internal sealed class AiModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<AiToolCall>(call =>
        {
            call.ToTable("AiToolCalls", "cw");
            call.Property(c => c.Channel).HasMaxLength(20);
            call.Property(c => c.Tool).HasMaxLength(100);
            call.Property(c => c.Input).HasMaxLength(4000);
            call.Property(c => c.Error).HasMaxLength(2000);
            call.HasIndex(c => new { c.TenantId, c.At });
        });
}
