namespace Coworkee.Contracts.Mailing;

public sealed record OutgoingMailDto(
    Guid Id, string To, string Subject, string TemplateName, OutgoingMailStatus Status, int Attempts, string? LastError, DateTimeOffset QueuedAt, DateTimeOffset? SentAt, Guid? CampaignId = null);
