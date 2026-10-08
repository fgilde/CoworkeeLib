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

    /// <summary>Rows of an uploaded Excel sheet become <typeparamref name="TCommand"/>s: columns map to its properties by name.</summary>
    public static IServiceCollection AddODataImport<TCommand>(this IServiceCollection services, string entitySet)
    {
        services.Registry().Add(Transfer.ODataImportRegistration.For<TCommand>(entitySet));
        return services;
    }

    /// <summary>Rows become <typeparamref name="TRow"/>s, e.g. the request record of an add command, and <paramref name="toCommand"/> wraps each one.</summary>
    public static IServiceCollection AddODataImport<TRow, TResult>(this IServiceCollection services, string entitySet, Func<TRow, Application.Messaging.IRequest<TResult>> toCommand)
        where TResult : Core.Results.Result
    {
        services.Registry().Add(Transfer.ODataImportRegistration.For(entitySet, toCommand));
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
