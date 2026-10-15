---
template: home.html
title: Coworkee
description: "Coworkee is a free .NET application framework: identity, permissions, multi-tenancy, a Blazor admin UI, reporting, AI and an Aspire app host as NuGet packages."
hide:
  - navigation
  - toc
---

## A taste

```csharp title="Catalog module"
[DependsOn(typeof(CoworkeeODataModule))]
public sealed class MyAppCatalogModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(MyAppCatalogModule).Assembly);
        context.Services.AddSingleton<IModelContributor, CatalogModelContributor>();
        context.Services.AddSingleton<IPermissionDefinitionContributor, CatalogPermissionDefinitions>();
        context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View);
    }

    public void ConfigureApplication(WebApplication app) => app.MapProductEndpoints();
}
```

```csharp title="App host"
var app = builder.AddCoworkeeApp("myapp", options => options.UseKeycloak());

app.AddMigrations<Projects.MyApp_Migrations>();
app.AddAuthServer<Projects.MyApp_Auth>();
app.AddApi<Projects.MyApp_Api>();
app.AddWeb<Projects.MyApp_Web>();
```

## Built on it

The [demo app](https://github.com/fgilde/Coworkee/tree/master) and [Sharemee](https://github.com/fgilde/sharemee) run on these packages; facets, response filters and code generation come from [Nextended](https://github.com/fgilde/Nextended).
