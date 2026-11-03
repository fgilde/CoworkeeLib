namespace Coworkee.Workflows;

/// <summary>Who may load a workflow item.</summary>
public enum WorkflowAccess
{
    /// <summary>Only users who see the entity through its OData set (permission, tenant, row filters).</summary>
    See,

    /// <summary>Those, and the approvers of the entity's open approval: they see what they decide.</summary>
    SeeOrDecide,

    /// <summary>No check: the system (deadline job).</summary>
    System,
}
