namespace Coworkee.Workflows;

public class WorkflowState(string name)
{
    public string Name { get; } = name;

    /// <summary>A CSS color for the state in facets and filters, e.g. <c>#2e7d32</c> or <c>var(--mud-palette-success)</c>.</summary>
    public string? Color { get; internal set; }

    internal List<Func<WorkflowContext, Task>> OnEnterActions { get; } = [];

    internal List<Func<WorkflowContext, Task>> OnExitActions { get; } = [];

    internal ApprovalStep? Step { get; set; }
}
