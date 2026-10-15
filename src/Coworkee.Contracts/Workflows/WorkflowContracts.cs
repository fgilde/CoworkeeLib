using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Workflows;

public static class WorkflowPermissions
{
    public const string GroupName = "Workflows";

    /// <summary>See states, history and the own approvals; the transitions bring their own guards.</summary>
    public const string View = "Workflows.View";

    /// <summary>Run transitions at all; each transition may ask for more (a permission, a role, a condition).</summary>
    public const string Transition = "Workflows.Transition";

    /// <summary>Design, run and inspect Elsa workflows (Coworkee.Workflows.Elsa).</summary>
    public const string Elsa = "Workflows.Elsa.Manage";
}

public static class WorkflowTopics
{
    /// <summary>Realtime topic that signals every transition, decision and reminder of one entity.</summary>
    public static string Entity(string entityType, Guid entityId) => $"workflow:{entityType}:{entityId}";
}

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

[JsonConverter(typeof(JsonStringEnumConverter<ApprovalStatus>))]
public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Expired,
    Canceled,
}

public sealed record WorkflowTransitionDto(string Name, string To, bool CommentRequired = false);

public sealed record WorkflowStepDto(WorkflowStepKind Kind, string? From, string? To, string? Transition, Guid? UserId, string? Comment, DateTimeOffset At);

public sealed record ApproverDto(Guid UserId, bool? Approved, string? Comment, DateTimeOffset? DecidedAt, bool Escalation);

public sealed record ApprovalDto(
    Guid Id, string EntityType, Guid EntityId, string State, string Title, bool RequiresAll, ApprovalStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset? DueAt, IReadOnlyList<ApproverDto> Approvers, bool CanDecide, string? Link);

/// <summary>Where an entity stands: <see cref="Transitions"/> are the ones the current user may run now.</summary>
public sealed record WorkflowStatusDto(
    string EntityType, Guid EntityId, string State, IReadOnlyList<WorkflowTransitionDto> Transitions, ApprovalDto? Approval, IReadOnlyList<WorkflowStepDto> History);

public sealed record TransitionRequest(string? Comment = null);

public sealed record DecisionRequest(bool Approve, string? Comment = null);
