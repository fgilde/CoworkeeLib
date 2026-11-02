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
app.UseCoworkee();                       // Exception-Handler, CORS, Request-Sprachen, jedes IWebModule, dann Authentifizierung, Autorisierung, Rate-Limits
```

Hosts ohne HTTP, etwa der Migrations-Dienst, rufen stattdessen `services.AddCoworkeeModules<TRoot>(configuration)` auf. Ein Worker-Host, der nur Hintergrundjobs ausführt, ruft `AddCoworkee<TRoot>()` und `app.UseCoworkeeWorker()` auf: Die Dienste der Module laufen, aber Endpunkte, Dashboards und OpenAPI werden nicht eingehängt.

Verschiedene Hosts setzen aus denselben Modulen verschiedene Graphen zusammen. Im Template hängt das API-Root von `MyAppDatabaseModule` ab, das jedes Modul der App mitbringt (mit Beispielen Katalog- und Dokumentenmodul), der Auth-Host nur von der Infrastruktur, `CoworkeeAuthServerModule` und mit Beispielen vom schlanken `MyAppRegistrationDocumentsModule`, das Registrierungsdokumente ohne die Dokumenten-API ablegt. Ein Modul, das nicht im Graphen eines Hosts liegt, kostet diesen Host nichts.

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

## Registrieren per Attribut

Statt `services.AddScoped<...>()` in `ConfigureServices` kann eine Klasse Nextendeds `[RegisterAs]` tragen. `AddCoworkeeModules` (und damit `AddCoworkee`) registriert die markierten Klassen jeder Modul-Assembly, nachdem die Module ihre Dienste konfiguriert haben:

```csharp
using Nextended.Core.Attributes;

[RegisterAs(typeof(IModelContributor), ServiceLifetime = ServiceLifetime.Singleton)]
internal sealed class CatalogModelContributor : IModelContributor { ... }

[RegisterAs(typeof(IPriceCalculator), ServiceLifetime = ServiceLifetime.Scoped)]
internal sealed class PriceCalculator : IPriceCalculator { ... }
```

| Eigenschaft | |
|---|---|
| `ServiceLifetime` | `Transient`, wenn nicht gesetzt |
| `ReplaceServices` | ersetzt, was ein Modul (oder die Bibliothek) für den Dienst registriert hat |
| `ServiceKey` | ein Keyed Service |
| `RegisterAsImplementation` | registriert die Klasse zusätzlich als sich selbst |

Durchsucht werden nur die Assemblies der Module; eine Registrierung, die schon da ist (gleicher Dienst, Key, Klasse und Lebensdauer), kommt nicht doppelt dazu, eine App, die `RegisterAllWithRegisterAsAttribute` selbst aufruft, läuft also weiter. Andere Assemblies rufen `services.AddRegisterAsServices(assembly)` auf (`Coworkee.Core.Modularity`, mit derselben Doppelprüfung); der Blazor-Client, der keine Module hat, ruft Nextendeds `builder.Services.RegisterAllWithRegisterAsAttribute(typeof(Program).Assembly)` auf. Beide Wege lassen sich mischen; `ConfigureServices` bleibt der Ort für Registrierungen mit Optionen oder Factories.

## Usings

Der meiste Code braucht wenige Namespaces:

```csharp
using Coworkee.Core.Modularity;          // CoworkeeModule, DependsOn, ModuleServiceContext
using Coworkee.AspNetCore;               // IWebModule, AddCoworkee, UseCoworkee
using Coworkee.Application.Messaging;    // AddMessagingFromAssembly, IDispatcher, ICommand, IQuery, IHandler
using Coworkee.Core.Results;             // Result, Error
```

[Pakete und Namespaces](../reference/packages.md) enthält die vollständige Liste.
