namespace Coworkee.Contracts.Mailing;

public sealed record MailCampaignDto(
    Guid Id,
    string Name,
    string TemplateName,
    RecipientSourceDto Recipients,
    MailCampaignStatus Status,
    DateTimeOffset? ScheduledFor,
    int Total,
    int Sent,
    int Failed,
    int Queued,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error);
