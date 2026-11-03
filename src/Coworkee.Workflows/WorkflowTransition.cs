namespace Coworkee.Workflows;

public class WorkflowTransition(string name, IReadOnlyList<string> from, string to)
{
    public string Name { get; } = name;

    public IReadOnlyList<string> From { get; } = from;

    public string To { get; } = to;

    internal string? Permission { get; set; }

    internal List<string> Roles { get; } = [];

    internal List<Func<WorkflowContext, Task<bool>>> Guards { get; } = [];

    internal List<Func<WorkflowContext, Task>> Actions { get; } = [];

    internal bool CommentRequired { get; set; }
}
