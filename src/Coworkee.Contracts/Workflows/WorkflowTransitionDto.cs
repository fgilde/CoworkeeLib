namespace Coworkee.Contracts.Workflows;

public sealed record WorkflowTransitionDto(string Name, string To, bool CommentRequired = false);
