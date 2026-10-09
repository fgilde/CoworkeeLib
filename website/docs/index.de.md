---
hide:
  - navigation
---

# Coworkee

Coworkee ist ein modulares Anwendungsframework für .NET 10. Es liefert einer Blazor- und ASP.NET-Core-Anwendung die Teile, die jede Geschäftsanwendung braucht, damit sich die App selbst um ihre Fachlichkeit kümmern kann: Module mit Abhängigkeiten, eine Request-Pipeline mit Validierung, Berechtigungen und Caching, EF Core mit Audit und Outbox, einen Auth-Server mit externem Login, eine Blazor-Oberfläche mit Datentabellen und Admin-Seiten und einen Aspire-AppHost, der alles verdrahtet.

![Die Demo-Anwendung auf Coworkee](assets/screenshots/products.png){ .shot }

<div class="grid cards" markdown>

-   **Neue App anlegen**

    CLI installieren, `coworkee new Shop`, `coworkee run`: eine vollständige App auf den NuGet-Paketen.

    [:octicons-arrow-right-24: Installation](getting-started/installation.md)

-   **Ein Feature bauen**

    Entity, Berechtigungen, Command, Endpunkt, OData-Set und eine Blazor-Seite mit Facetten, Schritt für Schritt.

    [:octicons-arrow-right-24: Das erste Feature](getting-started/first-feature.md)

-   **Die Bausteine verstehen**

    Module, der Dispatcher und seine Middleware, Konventionen der Persistenz.

    [:octicons-arrow-right-24: Grundlagen](fundamentals/modules.md)

-   **Die Oberfläche anpassen**

    Jede Komponente der Shell ersetzen, Navigation ergänzen, Tabellen bauen.

    [:octicons-arrow-right-24: Blazor-UI](ui/client.md)

</div>

## Was drin ist

| Bereich | Inhalt |
|---|---|
| Module | `CoworkeeModule` mit `[DependsOn]`, ein Aufruf `AddCoworkee<TRoot>()` pro Host |
| Requests | `IDispatcher` mit Commands, Queries, `Result`, FluentValidation, Berechtigungsprüfung, Caching, Unit of Work |
| Daten | `CoworkeeDbContext` mit Mandanten, Soft Delete, Audit auf Feldebene, transaktionaler Outbox, Realtime-Änderungen |
| HTTP | Minimal-API-Gruppen mit Problem Details, OData-Sets mit Facetten für jede Entity, Response-Filter |
| Identity | Benutzer, Rollen, Gruppen, Mandanten, Berechtigungen bis auf einzelne Ressourcen, Setup-Assistent oder Seed |
| Anmeldung | OpenIddict-Auth-Server, BFF für den Blazor-Client, Keycloak oder jeder OpenID-Connect-Anbieter als externer Login |
| Blazor | MudBlazor-Shell mit Navigation, Admin-Seiten, Datentabelle mit Facetten, Themes, ersetzbaren Komponenten |
| Dienste | Einstellungen, typisierte App-Konfiguration, Mail-Vorlagen, Hangfire-Jobs, Dateiablage, Elasticsearch, Benachrichtigungen, KI-Assistent mit MCP |
| Hosting | `AddCoworkeeApp` für Aspire: Postgres, Redis, Mailpit, Elasticsearch und Keycloak, verdrahtet anhand der Pakete jedes Dienstes |

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
