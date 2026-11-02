using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nextended.Core.Extensions;

namespace Coworkee.Core.Modularity;

public static class ModuleServiceCollectionExtensions
{
    /// <summary>
    /// Configures the modules in dependency order, then registers the types of their assemblies that carry Nextended's
    /// <see cref="Nextended.Core.Attributes.RegisterAsAttribute"/>.
    /// </summary>
    public static IReadOnlyList<CoworkeeModule> AddCoworkeeModules<TRoot>(this IServiceCollection services, IConfiguration configuration)
        where TRoot : CoworkeeModule
    {
        var modules = ModuleLoader.Resolve(typeof(TRoot))
            .Select(type => (CoworkeeModule)Activator.CreateInstance(type, nonPublic: true)!)
            .ToArray();

        var context = new ModuleServiceContext(services, configuration);
        foreach (var module in modules)
        {
            module.ConfigureServices(context);
        }

        services.AddRegisterAsServices(modules.Select(m => m.GetType().Assembly));
        services.AddSingleton<IReadOnlyList<CoworkeeModule>>(modules);
        return modules;
    }

    /// <summary>
    /// Registers the types of <paramref name="assemblies"/> that carry <see cref="Nextended.Core.Attributes.RegisterAsAttribute"/>; a registration
    /// that is there already (same service, key, implementation and lifetime) is not added twice.
    /// </summary>
    public static IServiceCollection AddRegisterAsServices(this IServiceCollection services, params IEnumerable<Assembly> assemblies)
    {
        var scan = assemblies.Distinct().ToArray();
        if (scan.Length == 0)
        {
            // Nextended would fall back to the calling and entry assemblies
            return services;
        }

        var added = new List<ServiceDescriptor>();
        services.RegisterAllWithRegisterAsAttribute(added.Add, scan);
        foreach (var descriptor in added)
        {
            if (services.Any(d => !ReferenceEquals(d, descriptor) && Identity(d) == Identity(descriptor)))
            {
                services.Remove(descriptor);
            }
        }

        return services;
    }

    private static (Type, object?, Type?, ServiceLifetime) Identity(ServiceDescriptor d) =>
        (d.ServiceType, d.ServiceKey, d.IsKeyedService ? d.KeyedImplementationType : d.ImplementationType, d.Lifetime);
}
