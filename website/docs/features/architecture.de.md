---
title: "Modularer Monolith mit ASP.NET Core"
description: "Coworkee baut .NET-Geschäftsanwendungen als modularen Monolithen auf ASP.NET Core: Module mit [DependsOn], eine Request-Pipeline, Outbox und eine CLI, die alles anlegt."
---

# Module und Werkzeuge

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

```csharp
builder.AddCoworkee<MyAppApiModule>();   // the root module; the rest follows from [DependsOn]
var app = builder.Build();
app.UseCoworkee();
```

Das ist das Dokumentenmodul aus dem Template, auf seinen Kern gekürzt (die Demo-App nutzt es vollständig), dazu die Zeilen, mit denen es im API-Host landet. Ein Modul registriert seine Services, hängt seine Endpunkte ein und sagt, welche Module es braucht. Coworkee lädt den Graphen einmal, in der Reihenfolge der Abhängigkeiten.

So entsteht ein modularer Monolith auf ASP.NET Core: ein Prozess pro Host, eine Datenbank, der Code nach Features geschnitten. API, Auth-Server und Migrationsdienst haben jeweils ihr eigenes Root-Modul und bekommen aus denselben Paketen verschiedene Graphen. Der Auth-Host lädt das Dokumentenmodul gar nicht und zahlt deshalb auch nichts dafür.

An zentrale Listen geht ein Modul auch nicht ran. Entity-Mappings kommen über `IModelContributor`, Menüeinträge über `INavigationContributor`. Fliegt ein Modul raus, verschwinden seine Beiträge mit, mehr ist nicht zu tun.

## Eine Pipeline für jeden Request

Jeder Anwendungsfall ist ein Command oder eine Query über `IDispatcher`. Endpunkte, Hintergrundjobs und der KI-Assistent schicken dieselben Requests, an Validierung und Berechtigungsprüfung führt also kein Seiteneingang vorbei.

| Reihenfolge | Schritt | |
|---|---|---|
| 100 | Logging | warnt, wenn ein Request länger als 500 ms dauert |
| 200 | Autorisierung | `[RequiresPermission]` am Request-Typ |
| 250 | Features | `[RequiresFeature]`, solange die Edition des Mandanten das Feature abgeschaltet hat |
| 300 | Validierung | FluentValidation, mit Feldmeldungen |
| 350 | Caching | `ICachedQuery` aus dem HybridCache, `IInvalidatesCache` leert Tags |
| 400 | Unit of Work | speichert den `DbContext` einmal nach einem erfolgreichen Command |

Bei erwarteten Fehlern geben Handler ein `Result` zurück, und `ToHttpResult` macht daraus 400, 401, 403, 404 oder 409. Eigenen übergreifenden Code hängst du als `IRequestMiddleware` ein, oder als typisiertes `IRequestBehavior`, wenn er nur einen Request betrifft.

Aggregate lösen Domain Events aus; die Handler laufen nach dem Commit über die Outbox, im Namen des Benutzers, der sie ausgelöst hat. Integration Events gehen über `IEventBus` durch dieselbe Outbox. Ohne Transport bleiben sie in der Anwendung. Mit `Coworkee.EventBus.RabbitMq` laufen sie über RabbitMQ, und eine Inbox-Tabelle sorgt dafür, dass kein Handler eine Nachricht zweimal verarbeitet.

Ausgeliefert wird alles als NuGet-Pakete mit dem Präfix `Coworkee.`, festgezurrt über eine `<CoworkeeVersion>` in `Directory.Packages.props`. `coworkee update` ändert diese eine Zahl.

## CLI, Studio und MCP-Server

```bash
dotnet tool install -g Coworkee.Cli
coworkee new Shop --no-samples --files --theme Forest
cd Shop
coworkee run
```

`coworkee new` legt die Solution an (Aspire-App-Host, API, Auth-Server, Blazor-Web-App, Migrationen), stellt die Pakete wieder her und erzeugt die erste Migration, falls die gewählten Module eine brauchen. Mit einer JSON-Spec kannst du die ganze Anwendung vorab beschreiben, eigene Module und Entitäten inklusive: `coworkee new --spec shop.json`. Später erzeugt `coworkee entity add` ein Feature von der Entität bis zur Blazor-Seite mit Datentabelle.

Wer lieber klickt, öffnet mit `coworkee ui` Coworkee Studio, einen Desktop-Assistenten für Windows, macOS und Linux. Zu jeder Option steht dort, was sie bringt und was sie kostet (die Seite Architektur fängt mit Vorlagen von *Minimal* bis *Alles* an). Außerdem zeigt das Studio, was aus einer Entität wird, und startet die neue Anwendung danach auf Wunsch.

![Entitäten-Designer in Coworkee Studio mit Eigenschaftsraster und Vorschau der erzeugten Routen und Dateien](../assets/screenshots/studio-entities-light-de.webp){ .shot loading=lazy }

Für Agenten gibt es `coworkee mcp`, einen Model-Context-Protocol-Server über stdio. In Claude Code trägst du ihn mit `claude mcp add coworkee -- coworkee mcp` ein; der Agent liest dann die Optionen, schreibt eine Spec, prüft sie und ruft dieselben Befehle auf, die du sonst tippst.

**Stand:** Coworkee ist bei Version 0.0.1.x. Many-to-many-Beziehungen kennen die Entity-Specs noch nicht, und von den Deployment-Zielen ist bisher nur Docker Compose durchgehend geprüft (siehe [Deployment](../hosting/deployment.md)).

Referenz: [Module](../fundamentals/modules.md), [Messaging und Pipeline](../fundamentals/messaging.md), [Events und Sperren](../fundamentals/events.md), [Kommandozeile](../getting-started/cli.md), [Coworkee Studio](../getting-started/studio.md), [MCP-Server der CLI](../getting-started/mcp.md).
