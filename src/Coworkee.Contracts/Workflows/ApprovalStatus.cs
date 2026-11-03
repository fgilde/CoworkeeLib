using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Workflows;

[JsonConverter(typeof(JsonStringEnumConverter<ApprovalStatus>))]
public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Expired,
    Canceled,
}
