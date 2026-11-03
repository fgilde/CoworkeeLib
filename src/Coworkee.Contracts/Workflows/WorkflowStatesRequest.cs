namespace Coworkee.Contracts.Workflows;

/// <summary>The rows of a table page whose workflow states to read in one request (at most 200).</summary>
public sealed record WorkflowStatesRequest(IReadOnlyList<Guid> Ids);
