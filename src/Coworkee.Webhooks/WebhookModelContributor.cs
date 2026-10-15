using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Webhooks;

internal sealed class WebhookModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebhookSubscription>(subscription =>
        {
            subscription.ToTable("WebhookSubscriptions", "cw");
            subscription.Property(s => s.Url).HasMaxLength(WebhookLimits.Url);
            subscription.Property(s => s.Description).HasMaxLength(500);
            subscription.Property(s => s.ProtectedSecret).HasMaxLength(1000).IsNotAudited();
            subscription.HasIndex(s => new { s.TenantId, s.IsActive });
        });
        modelBuilder.Entity<WebhookDelivery>(delivery =>
        {
            delivery.ToTable("WebhookDeliveries", "cw");
            delivery.Property(d => d.EventType).HasMaxLength(WebhookLimits.EventType);
            delivery.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            delivery.Property(d => d.Error).HasMaxLength(2000);
            delivery.HasIndex(d => new { d.TenantId, d.SubscriptionId, d.CreatedAt });
            delivery.HasIndex(d => new { d.SubscriptionId, d.EventId });
        });
    }
}

internal static class WebhookLimits
{
    public const int Url = 2000;
    public const int EventType = 200;
}
