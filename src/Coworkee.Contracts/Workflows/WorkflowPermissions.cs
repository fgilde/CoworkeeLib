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
