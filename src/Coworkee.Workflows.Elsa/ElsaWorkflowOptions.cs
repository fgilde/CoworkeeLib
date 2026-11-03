namespace Coworkee.Workflows.Elsa;

/// <summary>The entity types whose changes start Elsa workflows (the "Entity changed" trigger); workflow transitions and decisions always do.</summary>
public sealed class ElsaWorkflowOptions
{
    public const string Section = "Coworkee:Workflows:Elsa";

    internal Dictionary<Type, string> Watched { get; } = [];

    public ElsaWorkflowOptions Watch<TEntity>(string entityType)
        where TEntity : class
    {
        Watched[typeof(TEntity)] = entityType;
        return this;
    }
}
