using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Mailing;

[JsonConverter(typeof(JsonStringEnumConverter<MailCampaignStatus>))]
public enum MailCampaignStatus
{
    Draft,
    Scheduled,
    Sending,
    Sent,
    Failed,
    Cancelled,
}

[JsonConverter(typeof(JsonStringEnumConverter<RecipientPreset>))]
public enum RecipientPreset
{
    AllActive,
    NotSignedInSince,
    NeverSignedIn,
    SignedUpWithin,
    EmailNotConfirmed,
    InRoles,
    Inactive,
}

[JsonConverter(typeof(JsonStringEnumConverter<RecipientSourceKind>))]
public enum RecipientSourceKind
{
    Preset,
    Query,
    Manual,
}

public sealed record RecipientSourceDto(
    RecipientSourceKind Kind,
    RecipientPreset? Preset = null,
    int? Days = null,
    IReadOnlyList<Guid>? RoleIds = null,
    string? Filter = null,
    string? QueryJson = null,
    IReadOnlyList<Guid>? UserIds = null);

public sealed record RecipientRowDto(Guid Id, string Email, string? DisplayName, DateTimeOffset? LastLoginAt);

public sealed record RecipientPreviewDto(int Count, int Excluded, IReadOnlyList<RecipientRowDto> First);

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

public sealed record SaveMailCampaignRequest(string Name, string TemplateName, RecipientSourceDto Recipients);

public sealed record ScheduleMailCampaignRequest(DateTimeOffset? At);

public sealed record RecipientListDto(Guid Id, string Name, RecipientSourceDto Source);

public sealed record SaveRecipientListRequest(string Name, RecipientSourceDto Source);
