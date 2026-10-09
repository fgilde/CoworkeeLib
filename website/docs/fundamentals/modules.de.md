# Module

Ein Modul ist eine Klasse, die Dienste registriert und in Web-Hosts Endpunkte einhängt. Module erklären mit `[DependsOn]`, was sie brauchen; Coworkee lädt den ganzen Graphen einmal, in Abhängigkeitsreihenfolge.

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

| Member | Wann es läuft |
|---|---|
| `ConfigureServices(ModuleServiceContext)` | Während der Host seine Service-Collection aufbaut. `context.Configuration` ist die Host-Konfiguration. |
| `IWebModule.ConfigureApplication(WebApplication)` | Nachdem die App gebaut ist, in Modulreihenfolge. Endpunkte einhängen, Middleware ergänzen. |

## Ein Root pro Host

Ein Host nennt genau ein Root-Modul, der Rest ergibt sich aus den Abhängigkeiten:

```csharp
builder.AddCoworkee<MyAppApiModule>();   // registriert jedes Modul im Graphen
var app = builder.Build();
app.UseCoworkee();                       // Authentifizierung, Problem Details, dann jedes IWebModule
```

Hosts ohne HTTP, etwa der Migrations-Dienst, rufen stattdessen `services.AddCoworkeeModules<TRoot>(configuration)` auf.

Verschiedene Hosts setzen aus denselben Modulen verschiedene Graphen zusammen. Im Template hängt das API-Root von Katalog- und Dokumentenmodul ab, der Auth-Host nur von der Infrastruktur, `CoworkeeAuthServerModule` und mit Beispielen vom schlanken `MyAppRegistrationDocumentsModule`, das Registrierungsdokumente ohne die Dokumenten-API ablegt. Ein Modul, das nicht im Graphen eines Hosts liegt, kostet diesen Host nichts.

## Contributor statt zentraler Listen

Module ändern nie eine zentrale Liste. Sie fügen Contributor hinzu, die das zuständige Paket einsammelt:

| Contributor | Paket | Liefert |
|---|---|---|
| `IModelContributor` | Infrastructure | Entity-Mappings für den `DbContext` der App |
| `IPermissionDefinitionContributor` | Application | Berechtigungsgruppen und -namen |
| `ISettingDefinitionContributor` | Settings | Laufzeit-Einstellungen mit Gültigkeitsbereich und Typ |
| `IMailTemplateContributor` | Mailing | Mail-Vorlagen mit Standardtexten pro Sprache |
| `INavigationContributor` | Client.Blazor | Menüeinträge |
| `IAppBarContributor` | Client.Blazor | Komponenten in der App-Leiste |

Deshalb lässt sich ein Modul hinzufügen oder entfernen, ohne anderswo etwas anzufassen.

## Usings

Der meiste Code braucht wenige Namespaces:

```csharp
using Coworkee.Core.Modularity;          // CoworkeeModule, DependsOn, ModuleServiceContext
using Coworkee.AspNetCore;               // IWebModule, AddCoworkee, UseCoworkee
using Coworkee.Application.Messaging;    // AddMessagingFromAssembly, IDispatcher, ICommand, IQuery, IHandler
using Coworkee.Core.Results;             // Result, Error
```

[Pakete und Namespaces](../reference/packages.md) enthält die vollständige Liste.
