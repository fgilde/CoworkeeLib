---
hide:
  - navigation
---

# Coworkee

Coworkee ist ein modulares Anwendungsframework für .NET 10. Es liefert einer Blazor- und ASP.NET-Core-Anwendung die Teile, die jede Geschäftsanwendung braucht, damit sich die App selbst um ihre Fachlichkeit kümmern kann: Module mit Abhängigkeiten, eine Request-Pipeline mit Validierung, Berechtigungen und Caching, EF Core mit Audit und Outbox, einen Auth-Server mit externem Login, eine Blazor-Oberfläche mit Datentabellen und Admin-Seiten und einen Aspire-AppHost, der alles verdrahtet.

![Die Demo-Anwendung auf Coworkee](assets/screenshots/products.png){ .shot }

<div class="grid cards" markdown>

-   **Neue App anlegen**

    Template klonen, AppHost starten, mit dem geseedeten Administrator anmelden.

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

Coworkee ist aus [CleanArchitectureBaseBlazor](https://github.com/fgilde/CleanArchitectureBaseBlazor) entstanden. Das Template ist jetzt eine schlanke App auf den Paketen, und [Sharemee](https://github.com/fgilde/shareme), ein Digital-Asset-Management-System, ist die zweite App darauf. Die Oberfläche nutzt [MudBlazor](https://mudblazor.com) und [MudBlazor.Extensions](https://github.com/fgilde/MudBlazor.Extensions); Facetten, Response-Filter und Codegenerierung kommen aus [Nextended](https://github.com/fgilde/Nextended).

Wer ABP kennt, findet sich schnell zurecht: Module mit Abhängigkeiten, Berechtigungsdefinitionen, Einstellungen, ersetzbare Oberfläche. Coworkee verzichtet auf einiges an Zeremonie: keine Repositories über EF Core, keine Application-Service-Schicht, stattdessen Requests und Handler.
