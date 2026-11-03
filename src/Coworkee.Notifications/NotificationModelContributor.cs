using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Notifications;

internal sealed class NotificationModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(notification =>
        {
            notification.ToTable("Notifications", "cw");
            notification.Ignore(n => n.RealtimeTopics);
            notification.Property(n => n.Type).HasMaxLength(100);
            notification.Property(n => n.Title).HasMaxLength(300);
            notification.Property(n => n.Link).HasMaxLength(2000);
            notification.Property(n => n.Category).HasMaxLength(100);
            notification.HasIndex(n => new { n.UserId, n.ReadAt, n.CreatedAt });
            notification.HasIndex(n => new { n.UserId, n.DoneAt, n.Category });
            notification.HasIndex(n => new { n.ReadAt, n.CreatedAt });
        });

        modelBuilder.Entity<NotificationPreference>(preference =>
        {
            preference.ToTable("NotificationPreferences", "cw");
            preference.Property(p => p.Type).HasMaxLength(100);
            preference.Property(p => p.Scope).HasConversion<string>().HasMaxLength(10);
            preference.HasIndex(p => new { p.UserId, p.Type }).IsUnique();
            preference.HasIndex(p => p.Type);
        });

        modelBuilder.Entity<NotificationFollow>(follow =>
        {
            follow.ToTable("NotificationFollows", "cw");
            follow.Property(f => f.EntityType).HasMaxLength(100);
            follow.Property(f => f.Title).HasMaxLength(200);
            follow.HasIndex(f => new { f.UserId, f.EntityType, f.EntityId }).IsUnique();
            follow.HasIndex(f => new { f.EntityType, f.EntityId });
        });

        modelBuilder.Entity<PendingNotificationMail>(mail =>
        {
            mail.ToTable("PendingNotificationMails", "cw");
            mail.Property(m => m.Type).HasMaxLength(100);
            mail.Property(m => m.Key).HasMaxLength(200);
            mail.Property(m => m.Title).HasMaxLength(300);
            mail.Property(m => m.Link).HasMaxLength(2000);
            mail.HasIndex(m => m.DueAt);
            mail.HasIndex(m => new { m.UserId, m.Type, m.Key }).IsUnique();
        });

        modelBuilder.Entity<NotificationDigestState>(state =>
        {
            state.ToTable("NotificationDigests", "cw");
            state.HasIndex(s => s.UserId).IsUnique();
        });

        modelBuilder.Entity<Push.PushSubscription>(device =>
        {
            device.ToTable("PushSubscriptions", "cw");
            device.Property(s => s.Endpoint).HasMaxLength(2000);
            device.Property(s => s.P256dh).HasMaxLength(200);
            device.Property(s => s.Auth).HasMaxLength(200);
            device.Property(s => s.Device).HasMaxLength(200);
            device.HasIndex(s => s.Endpoint).IsUnique();
            device.HasIndex(s => s.UserId);
        });
    }
}
