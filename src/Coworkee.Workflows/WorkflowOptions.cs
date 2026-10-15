using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows;

/// <summary>The entity types that have a workflow; entity types are opt-in.</summary>
public sealed class WorkflowOptions
{
    internal Dictionary<string, EntityWorkflow> Workflows { get; } = new(StringComparer.Ordinal);

    public WorkflowOptions Entity<TEntity>(string entityType, Action<EntityWorkflow<TEntity>> configure)
        where TEntity : class
    {
        var workflow = new EntityWorkflow<TEntity>(entityType);
        configure(workflow);
        workflow.Validate();
        Workflows[entityType] = workflow;
        return this;
    }

    public EntityWorkflow? For(string entityType) => Workflows.GetValueOrDefault(entityType);
}

public static class WorkflowServiceCollectionExtensions
{
    /// <summary>
    /// Declares workflows, e.g. <c>services.AddCoworkeeWorkflows(w =&gt; w.Entity&lt;Product&gt;("Products", p =&gt; p.Initial("Draft").Transition("Submit", "Draft", "InReview")))</c>;
    /// may be called by several modules.
    /// </summary>
    public static IServiceCollection AddCoworkeeWorkflows(this IServiceCollection services, Action<WorkflowOptions> configure)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(WorkflowOptions))?.ImplementationInstance is not WorkflowOptions options)
        {
            options = new WorkflowOptions();
            services.AddSingleton(options);
        }

        configure(options);
        return services;
    }
}
