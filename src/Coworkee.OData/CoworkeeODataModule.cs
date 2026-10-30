using Coworkee.Application.Messaging;
using Coworkee.Application;
using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.OData.ClientEntities;
using Coworkee.OData.Transfer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;
using Nextended.Core.Facets;

namespace Coworkee.OData;

[DependsOn(typeof(CoworkeeApplicationModule), typeof(Coworkee.BackgroundJobs.CoworkeeBackgroundJobsModule))]
public sealed class CoworkeeODataModule : CoworkeeModule, IWebModule
{
    public const string RoutePrefix = "odata";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        var registry = services.Registry();
        services.AddMessagingFromAssembly(typeof(CoworkeeODataModule).Assembly);
        services.AddSingleton<Computed.ODataComputedProperties>();
        services.AddSingleton<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor, Computed.ComputedQueryRoots>();
        services.Routes();
        services.AddSingleton<ODataModels>();
        services.AddSingleton<IEdmModel>(sp => sp.GetRequiredService<ODataModels>().Of((string?)null));
        services.AddScoped<IODataQueries, ODataQueries>();
        services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<ODataOptions>, Computed.ODataRoute>();
        services.AddTransient<IFacetBuilder>(sp => new FacetBuilder(sp.GetRequiredService<IServiceScopeFactory>(), FacetLiterals.Options(), new FacetLiterals()));
        services.AddOptions<CoworkeeODataOptions>().Bind(context.Configuration.GetSection(CoworkeeODataOptions.Section))
            .Validate(o => o.MaxNodeCount >= 1, "Coworkee:OData:MaxNodeCount is at least 1.")
            .Validate(o => o.MaxAnyAllExpressionDepth >= 1, "Coworkee:OData:MaxAnyAllExpressionDepth is at least 1.")
            .Validate(o => o.MaxOrderByNodeCount >= 1, "Coworkee:OData:MaxOrderByNodeCount is at least 1.")
            .Validate(o => o.MaxExpansionDepth >= 0, "Coworkee:OData:MaxExpansionDepth is at least 0.")
            .Validate(o => o.FacetRowLimit >= 0, "Coworkee:OData:FacetRowLimit is at least 0.")
            .ValidateOnStart();
        services.AddDataTransfer();
        services.Configure<ClientEntityOptions>(context.Configuration.GetSection(ClientEntityOptions.Section));
        services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<ClientEntityOptions>, ServiceClientEntities>();
        services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<ODataOptions>, ODataQueryLimits>();
        services.AddSingleton<Microsoft.AspNetCore.Mvc.ApplicationModels.IApplicationModelProvider>(provider =>
            new EntityODataControllerNameProvider(registry, provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CoworkeeODataOptions>>().Value));
        services.AddControllers()
            .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new EntityODataControllerFeatureProvider(registry)))
            .AddOData();
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.MapControllers();
        app.MapDataTransferEndpoints();
        app.MapClientEntityEndpoints();
        GlobalSearch.GlobalSearchEndpoints.MapGlobalSearchEndpoints(app);
    }
}
