using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.EventBus;

internal sealed class InboxModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<InboxMessage>(entity =>
    {
        entity.ToTable("InboxMessages", "cw");
        entity.HasKey(e => new { e.MessageId, e.Handler });
        entity.Property(e => e.Handler).HasMaxLength(512);
    });
}
