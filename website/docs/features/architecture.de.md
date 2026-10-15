---
title: "Modularer Monolith mit ASP.NET Core"
description: "Coworkee baut .NET-Geschäftsanwendungen als modularen Monolithen auf ASP.NET Core: Module mit [DependsOn], eine Request-Pipeline, Outbox und eine CLI, die alles anlegt."
---

# Ein modularer Monolith für ASP.NET Core, samt Werkzeug zum Anlegen

Die meisten Geschäftsanwendungen beginnen als ein Projekt und enden als Knäuel: Jedes neue Feature ändert dieselbe Startup-Datei, denselben `DbContext` und dasselbe Menü. Coworkee teilt eine Anwendung vom ersten Tag an in Module, hält sie aber in einer auslieferbaren Einheit mit einer Datenbank. Sie bekommen die Grenzen von Services ohne Netzwerkaufrufe, verteilte Transaktionen und den Aufwand vieler Deployments.

Die zweite Hälfte des Problems ist der Start. Eine neue Solution mit API, Auth-Server, Blazor-Client, Migrationen und Aspire-App-Host ist ein Tag Verdrahtung, bevor die erste Entität existiert. Die Kommandozeile `coworkee`, ihr Desktop-Assistent Coworkee Studio und ein MCP-Server für KI-Agenten legen diese Solution in Minuten an, mit Ihren eigenen Modulen und Entitäten.

![Coworkee Studio auf der Seite Architektur, mit Vorlagen für den Anwendungstyp und den Infrastruktur-Optionen](../assets/screenshots/studio-architecture-light-de.webp){ .shot loading=lazy }

## Module mit `[DependsOn]`

Ein Modul ist eine Klasse, die ihre Services registriert und in Web-Hosts ihre Endpunkte einhängt. Mit `[DependsOn]` nennt es die Module, die es braucht; Coworkee lädt den ganzen Graphen einmal, in Abhängigkeitsreihenfolge:

```csharp
[DependsOn(typeof(CoworkeeODataModule), typeof(CoworkeeStorageModule))]
public sealed class MyAppDocumentsModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(MyAppDocumentsModule).Assembly);
        context.Services.AddODataEntity<Document>("Documents", DocumentPermissions.Documents.View, d => d.BlobKey);
    }

    public void ConfigureApplication(WebApplication app) => app.MapDocumentEndpoints();
}
```

Module ändern nie eine zentrale Liste. Entity-Mappings, Berechtigungen, Einstellungen, Mailvorlagen und Menüeinträge kommen über Contributors (`IModelContributor`, `IPermissionDefinitionContributor`, `ISettingDefinitionContributor`, `INavigationContributor` und einige mehr), die das zuständige Paket einsammelt. Ein Modul hinzuzufügen oder zu entfernen berührt nichts anderes.

## Ein Root-Modul pro Host

Jeder Server-Host im Template besteht aus denselben wenigen Zeilen. Das Root-Modul entscheidet, was der Host enthält:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddCoworkee<MyAppApiModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCoworkee();
app.Run();
```

API, Auth-Server und Migrationsdienst setzen aus denselben Modulen unterschiedliche Graphen zusammen. Der Auth-Host lädt nur Infrastruktur und das Auth-Server-Modul; ein Modul außerhalb des Graphen kostet den Host nichts. Hosts ohne HTTP rufen stattdessen `services.AddCoworkeeModules<TRoot>(configuration)` auf.

## Eine Request-Pipeline für jeden Anwendungsfall

Jeder Anwendungsfall ist ein Command oder eine Query, die über `IDispatcher` läuft. HTTP-Endpunkte, Hintergrundjobs, der KI-Assistent und Tests senden dieselben Requests, also gelten überall dieselben Regeln. Middleware umschließt jeden Request in fester Reihenfolge:

| Reihenfolge | Schritt | Was er tut |
|---|---|---|
| 100 | Logging | protokolliert den Request, warnt ab 500 ms |
| 200 | Autorisierung | prüft `[RequiresPermission]` am Request-Typ |
| 300 | Validierung | führt die FluentValidation-Validatoren aus, liefert Feldmeldungen |
| 350 | Caching | beantwortet `ICachedQuery` aus dem HybridCache, leert Tags nach `IInvalidatesCache` |
| 400 | Unit of Work | speichert den `DbContext` einmal nach einem erfolgreichen Command |

```csharp
[RequiresPermission(CatalogPermissions.Brands.Delete)]
public sealed record DeleteBrandsCommand(IReadOnlyList<Guid> Ids) : ICommand<Result>, IInvalidatesCache
{
    public IReadOnlyList<string> CacheTags => [DashboardCache.Tag];
}
```

Handler geben bei erwarteten Fehlern ein `Result` zurück, statt eine Exception zu werfen, und `ToHttpResult` macht aus Validierungs-, Nicht-gefunden-, Verboten- und Konfliktfehlern 400, 404, 403 und 409. Eigener übergreifender Code klinkt sich über `IRequestMiddleware` oder ein typisiertes `IRequestBehavior` ein.

## Domain Events, Integration Events und die Outbox

Aggregate lösen Domain Events aus; deren Handler laufen nach dem Commit über die Outbox, als der Benutzer, der sie verursacht hat. Integration Events überschreiten mit `IEventBus` die Grenzen einer Anwendung und nutzen dieselbe Outbox, ein Event geht also erst raus, wenn die Änderung dahinter gespeichert ist. Ohne Transport bekommen die Handler derselben Anwendung es direkt; mit `Coworkee.EventBus.RabbitMq` läuft es über RabbitMQ, mit Wiederholungen und Dead-Letter-Queue. Eine Inbox-Tabelle macht die Handler idempotent, und `IDistributedLock` verhindert, dass ein Job auf mehreren Instanzen gleichzeitig läuft.

## NuGet-Pakete statt kopierter Codebasis

Coworkee kommt als NuGet-Pakete, die alle mit `Coworkee.` beginnen: `Coworkee.Core`, `Coworkee.Application`, `Coworkee.Infrastructure`, `Coworkee.AspNetCore`, `Coworkee.Client.Blazor`, `Coworkee.Aspire` und darauf die Feature-Module. Eine erzeugte Anwendung referenziert sie mit einer Version in `Directory.Packages.props`, und `coworkee update` hebt diese Version an. Ihr Code bleibt in Ihren Projekten, der Framework-Code in Paketen, die Sie aktualisieren können.

## Die Kommandozeile `coworkee`

```bash
dotnet tool install -g Coworkee.Cli
coworkee new Shop --no-samples --files --theme Forest --primary-color "#0a7d5a"
cd Shop
coworkee run
```

`coworkee new` legt die Solution an (App-Host, API, Auth-Server, Blazor-Web-App, Migrationen), stellt die Pakete wieder her und erzeugt die erste Migration, wenn die gewählten Module eine brauchen. Eine JSON-Spec beschreibt die ganze Anwendung, inklusive eigener Module, Entitäten, Enums, Rollen und Entwicklungsbenutzer: `coworkee spec example`, `coworkee spec validate`, `coworkee new --spec shop.json`. Später erzeugen `coworkee module add` und `coworkee entity add` ein Feature-Modul oder eine Entität von vorn bis hinten: Contracts, Entität, Tabelle, Berechtigungen, OData-Set, Commands mit Validatoren, Endpunkte und eine Blazor-Seite mit Datentabelle. `coworkee doctor` prüft SDK, Docker und `dotnet-ef`.

## Coworkee Studio, der Desktop-Assistent

`coworkee ui` installiert und öffnet Coworkee Studio, das unter Windows, macOS und Linux läuft. Es führt Seite für Seite durch Grunddaten, Branding, Anmeldedesign, Architektur, Sprachen, Entitäten und Benutzer, erklärt, was jede Option bringt und kostet, und lässt die CLI die Solution anlegen. Die Seite Architektur bietet Vorlagen von *Minimal* bis *SaaS-Plattform*; die Seite Entitäten zeigt, was die CLI für jede Entität erzeugt. Nach dem Anlegen startet das Studio auf Wunsch den App-Host und verlinkt das Aspire-Dashboard und die Anwendung.

![Entitäten-Designer in Coworkee Studio mit Eigenschaftsraster und Vorschau der erzeugten Routen und Dateien](../assets/screenshots/studio-entities-light-de.webp){ .shot loading=lazy }

## Ein MCP-Server für KI-Agenten

`coworkee mcp` startet einen Model-Context-Protocol-Server über stdio. Claude Code, Claude Desktop oder VS Code können dann die Optionen und das Spec-Schema lesen, eine Spec schreiben und prüfen, die Anwendung anlegen und Module, Entitäten und Migrationen hinzufügen, mit denselben Befehlen, die Sie tippen würden:

```bash
claude mcp add coworkee -- coworkee mcp
```

Dieser Server läuft auf Ihrem Rechner. Eine erzeugte Anwendung hat ihren eigenen MCP-Endpunkt mit den Tools der Anwendung.

## Weiterlesen

- [Module](../fundamentals/modules.md): Lebenszyklus, Root-Module und Contributors
- [Messaging und Pipeline](../fundamentals/messaging.md): Commands, Queries, Results, Middleware und Caching
- [Events und Sperren](../fundamentals/events.md): Integration Events, RabbitMQ und verteilte Sperren
- [Aufbau der Solution](../getting-started/solution-structure.md): die Projekte einer erzeugten Anwendung
- [Kommandozeile](../getting-started/cli.md): alle Optionen von `coworkee new` und das Spec-Format
- [Coworkee Studio](../getting-started/studio.md): der Assistent Seite für Seite
- [MCP-Server der CLI](../getting-started/mcp.md): Registrierung und Tools
- [Pakete und Namespaces](../reference/packages.md): welches Paket wohin gehört
