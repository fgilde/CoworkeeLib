namespace Coworkee.Workflows;

/// <summary>The entity types that have a workflow; entity types are opt-in.</summary>
public sealed class WorkflowOptions
{
    internal Dictionary<string, EntityWorkflow> Workflows { get; } = new(StringComparer.Ordinal);

    public WorkflowOptions Entity<TEntity>(string entityType, Action<EntityWorkflow<TEntity>> configure)
        where TEntity : class
    {
        Coworkee.OData.ODataEntityAccess.RequireGuidId<TEntity>("Workflows");
        var workflow = new EntityWorkflow<TEntity>(entityType);
        configure(workflow);
        workflow.Validate();
        Workflows[entityType] = workflow;
        return this;
    }

    public EntityWorkflow? For(string entityType) => Workflows.GetValueOrDefault(entityType);
}
