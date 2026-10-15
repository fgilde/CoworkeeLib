---
template: home.html
title: Coworkee
description: "Coworkee ist ein kostenloses Anwendungsframework für .NET: Identity, Berechtigungen, Mandanten, Blazor-Admin-Oberfläche, Berichte, KI und Aspire-AppHost als NuGet-Pakete."
hide:
  - navigation
  - toc
---

## Ein erster Eindruck

```csharp title="Katalog-Modul"
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

```csharp title="AppHost"
var app = builder.AddCoworkeeApp("myapp", options => options.UseKeycloak());

app.AddMigrations<Projects.MyApp_Migrations>();
app.AddAuthServer<Projects.MyApp_Auth>();
app.AddApi<Projects.MyApp_Api>();
app.AddWeb<Projects.MyApp_Web>();
```

## Was darauf läuft

Die [Demo-App](https://github.com/fgilde/Coworkee/tree/master) und [Sharemee](https://github.com/fgilde/sharemee) laufen auf diesen Paketen; Facetten, Response-Filter und Codegenerierung kommen aus [Nextended](https://github.com/fgilde/Nextended).
