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

## Where Coworkee comes from

Coworkee started as my own project template: a [Blazor base template](https://github.com/fgilde/Coworkee/tree/release/last-standalone) with everything a business app needs, copied into every new app. Every app then carried its own copy of identity, permissions, settings, audit and admin pages, and every improvement had to be carried over by hand. So I turned the template into this library: the shared parts are NuGet packages now, and an app only holds its own domain.

The [demo app](https://github.com/fgilde/Coworkee/tree/master) is the former template rebuilt on the packages, and [Sharemee](https://github.com/fgilde/sharemee), a digital asset management system, is the second app built on them. The UI relies on [MudBlazor](https://mudblazor.com) and [MudBlazor.Extensions](https://www.mudex.org); facets, response filters and code generation come from [Nextended](https://github.com/fgilde/Nextended).
