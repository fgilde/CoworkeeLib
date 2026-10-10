using System.Reflection;
using Coworkee.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Coworkee.AspNetCore.ApplicationServices;

public static class ApplicationServiceExtensions
{
    /// <summary>
    /// Registers the application services the assemblies implement (scoped, unless the service is registered already). AddCoworkee
    /// does this for the assemblies of all modules.
    /// </summary>
    public static IServiceCollection AddCoworkeeApplicationServices(this IServiceCollection services, params IEnumerable<Assembly> assemblies)
    {
        foreach (var registration in assemblies.Distinct().SelectMany(ApplicationServiceRegistry.In))
        {
            if (services.Any(d => Equals(d.ImplementationInstance, registration)))
            {
                continue;
            }

            services.TryAddScoped(registration.ServiceType, registration.ImplementationType);
            services.AddSingleton(registration);
        }

        return services;
    }

    /// <summary>Maps the registered application services as endpoints; UseCoworkee does this.</summary>
    public static IEndpointRouteBuilder MapCoworkeeApplicationServices(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<ApplicationServiceOptions>>().Value;
        foreach (var registration in endpoints.ServiceProvider.GetServices<ApplicationServiceRegistration>())
        {
            var descriptor = options.Describe(registration);
            if (!descriptor.Enabled)
            {
                continue;
            }

            var group = endpoints.MapCoworkeeApi(descriptor.Route).WithTags(descriptor.Tag);
            group.AddEndpointFilter<RouteGroupBuilder, ApplicationServiceFilter>().ProducesValidationProblem();
            if (descriptor.RequireAuthorization)
            {
                group.RequireAuthorization();
            }

            foreach (var convention in descriptor.Conventions)
            {
                convention(group);
            }

            registration.Map(group);
        }

        return endpoints;
    }
}
