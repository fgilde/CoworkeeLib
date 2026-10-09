using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Features;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Features;

[DependsOn(typeof(CoworkeeIdentityModule))]
public sealed class CoworkeeFeaturesModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeFeaturesModule).Assembly);
        services.AddSingleton<IModelContributor, FeatureModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, FeaturePermissionDefinitions>();
        services.AddSingleton<IFeatureDefinitionManager, FeatureDefinitionManager>();
        services.AddScoped<IFeatureChecker, FeatureChecker>();
        services.AddScoped<HostAccess>();
        services.AddRequestMiddleware<FeatureMiddleware>();
        services.AddScoped<IInterceptor, FeatureCacheInterceptor>();
        services.AddODataEntity<Tenant>("Tenants", FeaturePermissions.Tenants);
        services.AddScoped<IODataEntityFilter<Tenant>, TenantODataFilter>();
        services.AddHybridCache();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var features = app.MapCoworkeeApi("/api/v1/features").WithTags("Features").RequireAuthorization();
        features.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetFeatures(), ct).ToHttpResult());
        features.MapGet("/definitions", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetFeatureDefinitions(), ct).ToHttpResult());

        var editions = app.MapCoworkeeApi("/api/v1/editions").WithTags("Editions").RequireAuthorization();
        editions.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetEditions(), ct).ToHttpResult());
        editions.MapPost("/", (EditionRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateEdition(body), ct).ToHttpResult());
        editions.MapPut("/{id:guid}", (Guid id, EditionRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateEdition(id, body), ct).ToHttpResult());
        editions.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteEdition(id), ct).ToHttpResult());

        var tenants = app.MapCoworkeeApi("/api/v1/tenants").WithTags("Tenants").RequireAuthorization();
        tenants.MapPost("/", (CreateTenantRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateTenant(body), ct).ToHttpResult());
        tenants.MapPut("/{id:guid}", (Guid id, TenantRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateTenant(id, body), ct).ToHttpResult());
        tenants.MapPost("/details", (IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetTenantDetails(body.Ids), ct).ToHttpResult());
        tenants.MapPut("/{id:guid}/features", (Guid id, TenantFeaturesRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetTenantFeatures(id, body), ct).ToHttpResult());
    }
}

internal sealed class FeaturePermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(FeaturePermissions.GroupName, "Tenants and editions")
            .Add(FeaturePermissions.Tenants, "Manage tenants")
            .Add(FeaturePermissions.Editions, "Manage editions");
}

internal sealed class TenantODataFilter(HostAccess host) : IODataEntityFilter<Tenant>
{
    public async Task<IQueryable<Tenant>> ApplyAsync(IQueryable<Tenant> query, CancellationToken cancellationToken) =>
        await host.IsHostAsync(cancellationToken) ? query : query.Where(_ => false);
}
