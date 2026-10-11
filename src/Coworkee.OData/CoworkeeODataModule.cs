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
using Microsoft.OData.ModelBuilder;
using Nextended.Core.Facets;
using Nextended.Web.Extensions;

namespace Coworkee.OData;

[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class CoworkeeODataModule : CoworkeeModule, IWebModule
{
    public const string RoutePrefix = "odata";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        var registry = services.Registry();
        services.AddMessagingFromAssembly(typeof(CoworkeeODataModule).Assembly);
        var model = new Lazy<IEdmModel>(() => BuildModel(registry));
        services.AddTransient<IFacetBuilder>(sp => new FacetBuilder(sp.GetRequiredService<IServiceScopeFactory>(), FacetLiterals.Options(), new FacetLiterals()));
        services.AddODataAuto(_ => model.Value);
        services.Configure<CoworkeeODataOptions>(context.Configuration.GetSection(CoworkeeODataOptions.Section));
        services.Configure<ClientEntityOptions>(context.Configuration.GetSection(ClientEntityOptions.Section));
        services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<ClientEntityOptions>, ServiceClientEntities>();
        services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<ODataOptions>, ODataQueryLimits>();
        services.AddSingleton<Microsoft.AspNetCore.Mvc.ApplicationModels.IApplicationModelProvider>(new EntityODataControllerNameProvider(registry));
        services.AddControllers()
            .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new EntityODataControllerFeatureProvider(registry)))
            .AddOData();
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.MapControllers();
        app.MapDataTransferEndpoints();
        app.MapClientEntityEndpoints();
    }

    private static IEdmModel BuildModel(ODataEntityRegistry registry)
    {
        var builder = new ODataConventionModelBuilder();
        foreach (var entity in registry.Entities)
        {
            var type = builder.AddEntityType(entity.EntityType);
            type.HasKey(entity.EntityType.GetProperty("Id") ?? throw new InvalidOperationException($"{entity.EntityType.Name} needs an Id property for OData."));
            foreach (var hidden in entity.HiddenProperties)
            {
                type.RemoveProperty(entity.EntityType.GetProperty(hidden)!);
            }

            builder.AddEntitySet(entity.EntitySet, type);
        }

        return builder.GetEdmModel();
    }
}
