namespace Coworkee.Contracts.Workflows;

public sealed record WorkflowStepDto(WorkflowStepKind Kind, string? From, string? To, string? Transition, Guid? UserId, string? Comment, DateTimeOffset At);
