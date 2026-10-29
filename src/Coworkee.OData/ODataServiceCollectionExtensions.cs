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

    /// <summary>An OData set whose properties <paramref name="configure"/> hides, e.g. <c>o =&gt; o.HideUnless(e =&gt; e.Salary, HrPermissions.Salaries)</c>.</summary>
    public static IServiceCollection AddODataEntity<TEntity>(
        this IServiceCollection services, string entitySet, string? permission, Action<ODataEntityOptions<TEntity>> configure)
        where TEntity : class
    {
        var options = new ODataEntityOptions<TEntity>();
        configure(options);
        services.Registry().Add(new ODataEntityRegistration(typeof(TEntity), entitySet, permission, options.Hidden) { ProtectedProperties = options.Protected, Key = options.KeyName ?? "Id" });
        return services;
    }

    /// <summary>
    /// Child rows follow their parent's row filters: <c>services.AddODataChildFilter&lt;TalkItem, Talk&gt;(i =&gt; i.Talk)</c> shows an item only when
    /// <see cref="ODataEntityAccess.VisibleAsync{TEntity}"/> of the talks contains its talk. Rows without a parent are hidden.
    /// </summary>
    public static IServiceCollection AddODataChildFilter<TChild, TParent>(this IServiceCollection services, Expression<Func<TChild, TParent?>> parent)
        where TChild : class
        where TParent : class =>
        services.AddScoped<IODataEntityFilter<TChild>>(provider => new ODataChildFilter<TChild, TParent>(provider, parent));

    /// <summary>Sets how the facet on a property looks and combines its values, like [FacetBehavior] on the property; this call wins.</summary>
    public static IServiceCollection ConfigureFacet<TEntity>(this IServiceCollection services, Expression<Func<TEntity, object?>> property, Action<Contracts.Data.FacetBehaviorAttribute> configure)
        where TEntity : class
    {
        var behavior = new Contracts.Data.FacetBehaviorAttribute();
        configure(behavior);
        services.Registry().Configure(typeof(TEntity), PropertyName(property), behavior);
        return services;
    }

    /// <summary>Lets clients mirror OData sets and query them locally, e.g. o.For&lt;Product&gt;(); entities marked [ClientEntity] need no call.</summary>
    public static IServiceCollection AddCoworkeeClientEntities(this IServiceCollection services, Action<ClientEntityOptions> configure) =>
        services.Configure(configure);

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

    /// <summary>
    /// Rows become create commands with a null id and update commands with the id of the row a key column found, e.g.
    /// <c>(Guid? id, AddEditBrandRequest row) =&gt; new AddEditBrandCommand(id, row)</c>; that enables update and upsert.
    /// With <paramref name="delete"/> the last import of a user can be undone: the rows it created are deleted again.
    /// </summary>
    public static IServiceCollection AddODataImport<TRow, TResult>(
        this IServiceCollection services, string entitySet, Func<Guid?, TRow, Application.Messaging.IRequest<TResult>> toCommand,
        Func<Guid, Application.Messaging.IRequest<Core.Results.Result>>? delete = null)
        where TResult : Core.Results.Result
    {
        services.Registry().Add(Transfer.ODataImportRegistration.For(entitySet, toCommand, delete));
        return services;
    }

    /// <summary>
    /// A second OData route over the same sets, e.g. <c>services.AddCoworkeeODataRoute("rt/odata", p =&gt; p.CamelCase().Alias&lt;IAudited&gt;(e =&gt; e.DateCreatedUtc, "creationTime"))</c>:
    /// its own model with the profile's names, and everything else of <c>/odata</c> (permissions, hidden and protected properties, row filters, navigation
    /// scope, query checks, search and facets).
    /// </summary>
    public static IServiceCollection AddCoworkeeODataRoute(this IServiceCollection services, string prefix, Action<ODataRouteProfile> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        configure(services.Routes().Add(prefix));
        return services;
    }

    internal static ODataRoutes Routes(this IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(ODataRoutes))?.ImplementationInstance is ODataRoutes routes)
        {
            return routes;
        }

        routes = new ODataRoutes();
        services.AddSingleton(routes);
        return routes;
    }

    internal static string PropertyName<TEntity>(Expression<Func<TEntity, object?>> property) =>
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
