using Coworkee.Contracts.Mailing;
using Coworkee.Domain;

namespace Coworkee.Mailing;

[NotAudited]
public sealed class MailCampaign : AggregateRoot
{
    public Guid? TenantId { get; set; }

    public required string Name { get; set; }

    public required string TemplateName { get; set; }

    public required string RecipientSource { get; set; }

    public MailCampaignStatus Status { get; set; }

    public DateTimeOffset? ScheduledFor { get; set; }

    public int Total { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? Error { get; set; }

    public void Schedule(DateTimeOffset? at)
    {
        Status = MailCampaignStatus.Scheduled;
        ScheduledFor = at;
        Raise(new MailCampaignScheduled(Id, at));
    }
}
