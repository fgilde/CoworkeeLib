namespace Coworkee.Workflows.Elsa;

public static class ElsaPermissions
{
    public const string GroupName = "Elsa";

    /// <summary>Design, run and inspect Elsa workflows through /api/elsa (and Elsa Studio).</summary>
    public const string Manage = Contracts.Workflows.WorkflowPermissions.Elsa;
}
