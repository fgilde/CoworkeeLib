namespace Coworkee.Workflows;

/// <summary>One move from <see cref="From"/> to <see cref="To"/>: a transition someone ran, or the workflow itself after a decision or a deadline.</summary>
internal sealed record WorkflowMove(string From, string To, string? Transition, string? Comment, Guid? UserId);
