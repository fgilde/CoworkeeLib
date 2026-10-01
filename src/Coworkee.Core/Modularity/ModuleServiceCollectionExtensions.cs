using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Core.Modularity;

public static class ModuleServiceCollectionExtensions
{
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

        services.AddSingleton<IReadOnlyList<CoworkeeModule>>(modules);
        return modules;
    }
}
