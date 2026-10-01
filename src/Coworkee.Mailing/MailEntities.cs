using Coworkee.Contracts.Mailing;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

public sealed class MailTemplateOverride : AuditedAggregateRoot
{
    public required string Name { get; set; }

    public Guid? TenantId { get; set; }

    public required string Culture { get; set; }

    public required string Subject { get; set; }

    public required string Body { get; set; }
}

public sealed record MailQueued(Guid MailId) : IDomainEvent;

[NotAudited]
public sealed class OutgoingMail : AggregateRoot
{
    private OutgoingMail()
    {
    }

    public string To { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string HtmlBody { get; private set; } = string.Empty;

    public string TemplateName { get; private set; } = string.Empty;

    public Guid? TenantId { get; private set; }

    public OutgoingMailStatus Status { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset QueuedAt { get; private set; }

    public DateTimeOffset? SentAt { get; set; }

    public static OutgoingMail Queue(string to, RenderedMail mail, string templateName, Guid? tenantId, DateTimeOffset now)
    {
        var outgoing = new OutgoingMail
        {
            To = to,
            Subject = mail.Subject,
            HtmlBody = mail.HtmlBody,
            TemplateName = templateName,
            TenantId = tenantId,
            Status = OutgoingMailStatus.Queued,
            QueuedAt = now,
        };
        outgoing.Raise(new MailQueued(outgoing.Id));
        return outgoing;
    }
}

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

        modelBuilder.Entity<OutgoingMail>(mail =>
        {
            mail.ToTable("OutgoingMails", "cw");
            mail.Property(m => m.To).HasMaxLength(320);
            mail.Property(m => m.Subject).HasMaxLength(500);
            mail.Property(m => m.TemplateName).HasMaxLength(200);
            mail.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
            mail.Property(m => m.LastError).HasMaxLength(2000);
            mail.HasIndex(m => new { m.TenantId, m.QueuedAt });
        });
    }
}
