using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData.ClientEntities;

internal sealed class ClientEntityTouchModel : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<ClientEntityTouch>(touch =>
    {
        touch.ToTable("ClientEntityTouches", "cw");
        touch.Property(t => t.EntityType).HasMaxLength(300);
        touch.HasIndex(t => new { t.EntityType, t.TouchedAt });
    });
}
