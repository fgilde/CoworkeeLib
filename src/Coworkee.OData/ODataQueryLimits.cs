using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Query.Validator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.OData;

internal sealed class ODataQueryLimits(IOptions<CoworkeeODataOptions> options) : IPostConfigureOptions<ODataOptions>
{
    public void PostConfigure(string? name, ODataOptions odata) => odata.SetMaxTop(options.Value.MaxTop);
}

/// <summary>
/// The query limits of <see cref="CoworkeeODataOptions"/> (node count, any/all depth, expand depth, order by properties) for a $filter, $orderby or
/// $expand a caller sent that the library parses outside an entity set's route: exports, query_data, row filters of code, facet filters, reports.
/// </summary>
public static class ODataLimits
{
    /// <summary>The configured limits as OData's validation settings.</summary>
    public static ODataValidationSettings Settings(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<CoworkeeODataOptions>>().Value;
        return new ODataValidationSettings
        {
            MaxNodeCount = options.MaxNodeCount,
            MaxAnyAllExpressionDepth = options.MaxAnyAllExpressionDepth,
            MaxExpansionDepth = options.MaxExpansionDepth,
            MaxOrderByNodeCount = options.MaxOrderByNodeCount,
        };
    }

    /// <summary>Checks the $filter, $orderby and $expand of <paramref name="options"/>; a query beyond a limit throws an ODataException.</summary>
    public static void Check(ODataQueryOptions options, IServiceProvider services)
    {
        var settings = Settings(services);
        options.Filter?.Validate(settings);
        options.OrderBy?.Validate(settings);
        options.SelectExpand?.Validate(settings);
    }
}
