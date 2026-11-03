using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Mailing;

[JsonConverter(typeof(JsonStringEnumConverter<OutgoingMailStatus>))]
public enum OutgoingMailStatus
{
    Queued,
    Sending,
    Sent,
    Failed,
    Skipped,
}
