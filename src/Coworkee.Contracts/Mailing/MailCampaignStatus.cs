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
