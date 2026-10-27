using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Formatter.Serialization;
using Microsoft.AspNetCore.OData.Query.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Coworkee.OData.Computed;

/// <summary>The route <c>/odata</c> like Nextended's AddODataAuto, plus the binders and the serializer of computed properties.</summary>
internal sealed class ODataRoute(IEdmModel model, ODataComputedProperties computed, IOptions<CoworkeeODataOptions> coworkee) : IConfigureOptions<ODataOptions>
{
    public void Configure(ODataOptions options)
    {
        // "filter=" without $ would pass the checks that read $filter, $orderby and $expand
        options.EnableNoDollarQueryOptions = false;
        options.EnableQueryFeatures().AddRouteComponents(CoworkeeODataModule.RoutePrefix, model, services =>
        {
            services.AddSingleton<IODataSerializerProvider>(sp => new ComputedSerializerProvider(sp, computed));
            services.AddSingleton<ODataUriResolver>(_ => new ODataUriResolver { EnableCaseInsensitive = coworkee.Value.CaseInsensitive });
            var filter = new ComputedFilterBinder(computed);
            var orderBy = new ComputedOrderByBinder(computed);
            services.AddSingleton<IFilterBinder>(filter);
            services.AddSingleton<IOrderByBinder>(orderBy);
            services.AddSingleton<ISelectExpandBinder>(new ComputedSelectExpandBinder(filter, orderBy, computed));
        });
    }
}
