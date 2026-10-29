using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Formatter.Serialization;
using Microsoft.AspNetCore.OData.Query.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Coworkee.OData.Computed;

/// <summary>
/// Every OData route (<c>/odata</c> and the profiles) like Nextended's AddODataAuto, plus the binders and the serializer of computed properties:
/// the same components on each, only the model differs.
/// </summary>
internal sealed class ODataRoute(ODataRoutes routes, ODataModels models, ODataComputedProperties computed, IOptions<CoworkeeODataOptions> coworkee) : IConfigureOptions<ODataOptions>
{
    public void Configure(ODataOptions options)
    {
        // "filter=" without $ would pass the checks that read $filter, $orderby and $expand
        options.EnableNoDollarQueryOptions = false;
        options.EnableQueryFeatures();
        foreach (var profile in routes.Profiles)
        {
            Add(options, profile.Prefix, models.Of(profile));
        }
    }

    private void Add(ODataOptions options, string prefix, IEdmModel model) =>
        options.AddRouteComponents(prefix, model, services =>
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
