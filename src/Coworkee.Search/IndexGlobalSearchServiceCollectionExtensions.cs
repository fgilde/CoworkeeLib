using Coworkee.Application.GlobalSearch;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Search;

public static class IndexGlobalSearchServiceCollectionExtensions
{
    /// <summary>Lets the global search find documents of an index alias, e.g. <c>services.AddGlobalSearchIndex("products", s =&gt; s.Fields("name", "barcode").Url("/catalog/products?id={id}"))</c>.</summary>
    public static IServiceCollection AddGlobalSearchIndex(this IServiceCollection services, string alias, Action<IndexSearchOptions> configure)
    {
        var options = new IndexSearchOptions();
        configure(options);
        if (options.SearchFields.Count == 0)
        {
            throw new ArgumentException($"The global search of the index {alias} needs at least one field.", nameof(configure));
        }

        return services.AddGlobalSearchProvider(new IndexSearchProvider(alias, options));
    }
}
