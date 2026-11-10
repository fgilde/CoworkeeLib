using Coworkee.Application.GlobalSearch;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.GlobalSearch;

public static class ODataGlobalSearchServiceCollectionExtensions
{
    /// <summary>
    /// Lets the global search find records of a set registered with AddODataEntity: same permission, organisation and row filters as its
    /// OData list, e.g. <c>services.AddGlobalSearch&lt;Product&gt;(s =&gt; s.Fields(p =&gt; p.Name, p =&gt; p.Barcode).Subtitle(p =&gt; p.Barcode).Page("/catalog/products"))</c>.
    /// Without <see cref="ODataSearchOptions{T}.Fields"/> it searches the fields the set declares (<see cref="ODataEntityOptions{TEntity}.SearchFields"/>).
    /// </summary>
    public static IServiceCollection AddGlobalSearch<T>(this IServiceCollection services, Action<ODataSearchOptions<T>> configure)
        where T : class
    {
        var options = new ODataSearchOptions<T>();
        configure(options);
        var registry = services.Registry();
        if (options.SearchFields.Count > 0)
        {
            // $search and the data tables look in the same fields, unless the set declares its own
            registry.GlobalSearchFields(typeof(T), [.. options.SearchFields]);
        }

        return services.AddSingleton<IGlobalSearchProvider>(sp =>
        {
            // the set may be registered after this call
            if (options.SearchFields.Count == 0 && registry.EntitySearchFields(typeof(T)) is { } declared)
            {
                options.Fields([.. declared.Cast<System.Linq.Expressions.Expression<Func<T, string?>>>()]);
            }

            return options.SearchFields.Count == 0
                ? throw new InvalidOperationException($"The global search of {typeof(T).Name} needs at least one field: name them in AddGlobalSearch or with SearchFields in AddODataEntity.")
                : new ODataSearchProvider<T>(registry, options);
        });
    }
}
