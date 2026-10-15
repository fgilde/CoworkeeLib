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

## Woher Coworkee kommt

Coworkee war zuerst mein eigenes Projekt-Template: ein [Blazor-Basis-Template](https://github.com/fgilde/Coworkee/tree/release/last-standalone) mit allem, was eine Business-App braucht, kopiert in jede neue App. Jede App trug dann ihre eigene Kopie von Identity, Berechtigungen, Einstellungen, Audit und Administrationsseiten, und jede Verbesserung musste von Hand nachgezogen werden. Deshalb habe ich aus dem Template diese Bibliothek gebaut: Die gemeinsamen Teile sind jetzt NuGet-Pakete, und eine App enthält nur noch ihre eigene Fachlichkeit.

Die [Demo-App](https://github.com/fgilde/Coworkee/tree/master) ist das frühere Template, neu gebaut auf den Paketen, und [Sharemee](https://github.com/fgilde/sharemee), ein Digital-Asset-Management-System, ist die zweite App darauf. Die Oberfläche nutzt [MudBlazor](https://mudblazor.com) und [MudBlazor.Extensions](https://www.mudex.org); Facetten, Response-Filter und Codegenerierung kommen aus [Nextended](https://github.com/fgilde/Nextended).
