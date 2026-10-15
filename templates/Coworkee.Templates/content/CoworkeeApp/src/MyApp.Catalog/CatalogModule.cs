using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
#if (webhooks)
using Coworkee.Webhooks;
#endif
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Catalog.Domain;
using MyApp.Catalog.Features.Brands.Commands.AddEdit;
using MyApp.Catalog.Features.Brands.Commands.Delete;
using MyApp.Catalog.Features.Products.Commands.AddEdit;
using MyApp.Catalog.Features.Products.Commands.Delete;
#if (workflows)
using MyApp.Catalog.Features.Products;
#endif
using MyApp.Catalog.Endpoints;
using MyApp.Catalog.Permissions;
using MyApp.Catalog.Persistence;
using MyApp.Contracts.Catalog;

namespace MyApp.Catalog;

[DependsOn(typeof(CoworkeeODataModule))]
public sealed class MyAppCatalogModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(MyAppCatalogModule).Assembly);
        context.Services.AddSingleton<IModelContributor, CatalogModelContributor>();
        context.Services.AddSingleton<IPermissionDefinitionContributor, CatalogPermissionDefinitions>();
        context.Services.AddODataEntity<Brand>("Brands", CatalogPermissions.Brands.View);
        context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View);
#if (webhooks)
        context.Services.AddCoworkeeWebhooks(webhooks => webhooks.Entity<Product>());
#endif
        context.Services.AddODataImport("Brands", (Guid? id, AddEditBrandRequest row) => new AddEditBrandCommand(id, row), id => new DeleteBrandsCommand([id]));
        context.Services.AddODataImport("Products", (Guid? id, AddEditProductRequest row) => new AddEditProductCommand(id, row), id => new DeleteProductsCommand([id]));
#if (workflows)
        context.Services.AddProductApproval();
#endif
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.MapBrandEndpoints();
        app.MapProductEndpoints();
        app.MapDashboardEndpoints();
    }
}
