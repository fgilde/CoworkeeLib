namespace Coworkee.Contracts.Workflows;

public static class WorkflowTopics
{
    /// <summary>Realtime topic that signals every transition, decision and reminder of one entity.</summary>
    public static string Entity(string entityType, Guid entityId) => $"workflow:{entityType}:{entityId}";
}
