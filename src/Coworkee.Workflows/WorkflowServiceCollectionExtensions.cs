using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows;

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
