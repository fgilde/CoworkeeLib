using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Workflows;

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowStepKind>))]
public enum WorkflowStepKind
{
    Transition,
    Approved,
    Rejected,
    Reminded,
    Escalated,
    Expired,
}
