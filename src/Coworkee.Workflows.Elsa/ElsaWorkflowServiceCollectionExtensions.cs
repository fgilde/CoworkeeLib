using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows.Elsa;

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
