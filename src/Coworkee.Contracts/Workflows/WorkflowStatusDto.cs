namespace Coworkee.Contracts.Workflows;

/// <summary>Where an entity stands: <see cref="Transitions"/> are the ones the current user may run now.</summary>
public sealed record WorkflowStatusDto(
    string EntityType, Guid EntityId, string State, IReadOnlyList<WorkflowTransitionDto> Transitions, ApprovalDto? Approval, IReadOnlyList<WorkflowStepDto> History);
