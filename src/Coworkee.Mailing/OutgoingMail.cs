using Coworkee.Contracts.Mailing;
using Coworkee.Domain;

namespace Coworkee.Mailing;

[NotAudited]
public sealed class OutgoingMail : AggregateRoot
{
    private OutgoingMail()
    {
    }

    // public setters on what the mail log shows, searches and sorts: the OData convention leaves out properties without one
    public string To { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string HtmlBody { get; private set; } = string.Empty;

    [Nextended.Core.Facets.ProvideFacet(Label = "Template")]
    public string TemplateName { get; set; } = string.Empty;

    public Guid? TenantId { get; private set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "Status")]
    public OutgoingMailStatus Status { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public Guid? CampaignId { get; set; }

    public DateTimeOffset QueuedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>When the last attempt took the mail into <see cref="OutgoingMailStatus.Sending"/>; <see cref="StuckMailSweep"/> measures from it.</summary>
    public DateTimeOffset? SendingSince { get; set; }

    public static OutgoingMail Queue(string to, RenderedMail mail, string templateName, Guid? tenantId, DateTimeOffset now, Guid? campaignId = null)
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
            CampaignId = campaignId,
        };
        outgoing.Raise(new MailQueued(outgoing.Id));
        return outgoing;
    }
}
