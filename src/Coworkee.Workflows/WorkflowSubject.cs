namespace Coworkee.Workflows;

/// <summary>An entity with its workflow and state, loaded for one request.</summary>
public sealed record WorkflowSubject(EntityWorkflow Workflow, object Entity, WorkflowItem Item);
