using Coworkee.Application.GlobalSearch;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.GlobalSearch;

public static class ODataGlobalSearchServiceCollectionExtensions
{
    /// <summary>
    /// Lets the global search find records of a set registered with AddODataEntity: same permission, organisation and row filters as its
    /// OData list, e.g. <c>services.AddGlobalSearch&lt;Product&gt;(s =&gt; s.Fields(p =&gt; p.Name, p =&gt; p.Barcode).Subtitle(p =&gt; p.Barcode).Page("/catalog/products"))</c>.
    /// </summary>
    public static IServiceCollection AddGlobalSearch<T>(this IServiceCollection services, Action<ODataSearchOptions<T>> configure)
        where T : class
    {
        var options = new ODataSearchOptions<T>();
        configure(options);
        if (options.SearchFields.Count == 0)
        {
            throw new ArgumentException($"The global search of {typeof(T).Name} needs at least one field.", nameof(configure));
        }

        // $search on the set looks in the same fields
        services.Registry().SearchFields(typeof(T), [.. options.SearchFields]);
        return services.AddSingleton<IGlobalSearchProvider>(sp => new ODataSearchProvider<T>(sp.GetRequiredService<ODataEntityRegistry>(), options));
    }
}
