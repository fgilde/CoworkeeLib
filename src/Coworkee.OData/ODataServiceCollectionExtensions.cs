using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData;

public static class ODataServiceCollectionExtensions
{
    /// <param name="hidden">Properties that stay out of the EDM: never serialized, filtered or sorted on.</param>
    public static IServiceCollection AddODataEntity<TEntity>(
        this IServiceCollection services, string entitySet, string? permission = null, params Expression<Func<TEntity, object?>>[] hidden)
        where TEntity : class
    {
        services.Registry().Add(new ODataEntityRegistration(typeof(TEntity), entitySet, permission, [.. hidden.Select(PropertyName)]));
        return services;
    }

    private static string PropertyName<TEntity>(Expression<Func<TEntity, object?>> property) =>
        (property.Body is UnaryExpression { Operand: MemberExpression boxed } ? boxed : property.Body as MemberExpression)?.Member.Name
        ?? throw new ArgumentException($"{property} does not select a property.", nameof(property));

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
