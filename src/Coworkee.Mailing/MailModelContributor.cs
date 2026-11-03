using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

internal sealed class MailModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MailTemplateOverride>(template =>
        {
            template.ToTable("MailTemplateOverrides", "cw");
            template.Property(t => t.Name).HasMaxLength(200);
            template.Property(t => t.Culture).HasMaxLength(20);
            template.Property(t => t.Subject).HasMaxLength(500);
            template.HasIndex(t => new { t.Name, t.TenantId, t.Culture }).IsUnique().AreNullsDistinct(false);
        });

        modelBuilder.Entity<CustomMailTemplate>(template =>
        {
            template.ToTable("CustomMailTemplates", "cw");
            template.Property(t => t.Name).HasMaxLength(128);
            template.Property(t => t.DisplayName).HasMaxLength(256);
            template.Property(t => t.Description).HasMaxLength(1000);
            template.HasIndex(t => new { t.TenantId, t.Name }).IsUnique().AreNullsDistinct(false);
        });

        modelBuilder.Entity<OutgoingMail>(mail =>
        {
            mail.ToTable("OutgoingMails", "cw");
            mail.Property(m => m.To).HasMaxLength(320);
            mail.Property(m => m.Subject).HasMaxLength(500);
            mail.Property(m => m.TemplateName).HasMaxLength(200);
            mail.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
            mail.Property(m => m.LastError).HasMaxLength(2000);
            mail.HasIndex(m => new { m.TenantId, m.QueuedAt });
            mail.HasIndex(m => m.CampaignId);
            mail.HasIndex(m => m.Status);
        });

        modelBuilder.Entity<OutgoingMailAttachment>(attachment =>
        {
            attachment.ToTable("OutgoingMailAttachments", "cw");
            attachment.Property(a => a.FileName).HasMaxLength(255);
            attachment.Property(a => a.ContentType).HasMaxLength(200);
            attachment.HasOne<OutgoingMail>().WithMany().HasForeignKey(a => a.MailId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MailCampaign>(campaign =>
        {
            campaign.ToTable("MailCampaigns", "cw");
            campaign.Property(c => c.Name).HasMaxLength(256);
            campaign.Property(c => c.TemplateName).HasMaxLength(128);
            campaign.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            campaign.Property(c => c.Error).HasMaxLength(2000);
            campaign.HasIndex(c => new { c.TenantId, c.CreatedAt });
        });

        modelBuilder.Entity<RecipientList>(list =>
        {
            list.ToTable("MailRecipientLists", "cw");
            list.Property(l => l.Name).HasMaxLength(256);
            list.HasIndex(l => new { l.TenantId, l.Name }).IsUnique().AreNullsDistinct(false);
        });
    }
}
