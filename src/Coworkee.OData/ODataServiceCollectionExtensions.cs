using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData;

public static class ODataServiceCollectionExtensions
{
    public static IServiceCollection AddODataEntity<TEntity>(this IServiceCollection services, string entitySet, string? permission = null)
        where TEntity : class
    {
        services.Registry().Add(new ODataEntityRegistration(typeof(TEntity), entitySet, permission));
        return services;
    }

    internal static ODataEntityRegistry Registry(this IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(ODataEntityRegistry))?.ImplementationInstance is ODataEntityRegistry registry)
        {
            return registry;
        }

        registry = new ODataEntityRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}
