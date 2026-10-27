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
using Microsoft.Extensions.Logging;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
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
        services.AddSingleton<IEdmModel>(sp => BuildModel(registry, sp.GetRequiredService<Computed.ODataComputedProperties>(), sp.GetService<ILoggerFactory>()?.CreateLogger<CoworkeeODataModule>()));
        services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<ODataOptions>, Computed.ODataRoute>();
        services.AddTransient<IFacetBuilder>(sp => new FacetBuilder(sp.GetRequiredService<IServiceScopeFactory>(), FacetLiterals.Options(), new FacetLiterals()));
        services.Configure<CoworkeeODataOptions>(context.Configuration.GetSection(CoworkeeODataOptions.Section));
        services.AddDataTransfer();
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
        GlobalSearch.GlobalSearchEndpoints.MapGlobalSearchEndpoints(app);
    }

    private static IEdmModel BuildModel(ODataEntityRegistry registry, Computed.ODataComputedProperties computed, ILogger? logger)
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

        var model = (EdmModel)builder.GetEdmModel();
        foreach (var entity in registry.Entities)
        {
            var type = (IEdmStructuredType)model.FindDeclaredType(entity.EntityType.FullName);
            foreach (var hidden in entity.HiddenProperties.Where(h => type.FindProperty(h) is not null))
            {
                entity.SharedHidden.Add(hidden);
                logger?.LogWarning(
                    "{Set}: {Property} is declared on {Base}, a type the OData model shares with other sets, so Hide cannot remove it; it stays in the model and is refused on {Set} for everyone.",
                    entity.EntitySet, hidden, type.FindProperty(hidden).DeclaringType.FullTypeName(), entity.EntitySet);
            }
        }

        computed.AddTo(model);
        return model;
    }
}
