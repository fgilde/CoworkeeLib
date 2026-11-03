using Coworkee.Domain;

namespace Coworkee.Mailing;

public sealed record MailCampaignScheduled(Guid CampaignId, DateTimeOffset? At) : IDomainEvent;
