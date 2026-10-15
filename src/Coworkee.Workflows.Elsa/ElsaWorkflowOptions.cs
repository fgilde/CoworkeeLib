using Microsoft.Extensions.DependencyInjection;

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

public static class ElsaWorkflowServiceCollectionExtensions
{
    /// <summary>E.g. <c>services.AddCoworkeeElsa(e =&gt; e.Watch&lt;Product&gt;("Products"))</c>; may be called by several modules.</summary>
    public static IServiceCollection AddCoworkeeElsa(this IServiceCollection services, Action<ElsaWorkflowOptions> configure)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(ElsaWorkflowOptions))?.ImplementationInstance is not ElsaWorkflowOptions options)
        {
            options = new ElsaWorkflowOptions();
            services.AddSingleton(options);
        }

        configure(options);
        return services;
    }
}
