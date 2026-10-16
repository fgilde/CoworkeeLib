# Elsa-Workflows

`Coworkee.Workflows.Elsa` hostet [Elsa Workflows 3](https://elsa-workflows.github.io/elsa-documentation/) (MIT, 3.9 mit Builds für .NET 10) in der API einer Coworkee-App. Nimm es, wenn Administratoren lang laufende Abläufe selbst bauen sollen, über die im Code deklarierten Zustände und Genehmigungen von [Workflows und Genehmigungen](workflows.md) hinaus: „Wenn ein Produkt genehmigt ist, zwei Tage warten, die offenen Bestellungen abfragen und den Vertrieb benachrichtigen."

```csharp
[DependsOn(typeof(MyAppDatabaseModule), typeof(CoworkeeWorkflowsElsaModule))]
public sealed class MyAppApiModule : CoworkeeModule, IWebModule { ... }
```

Nimm es nur in den API-Host auf: Elsa startet dort seine eigenen Hintergrunddienste.

## Was es einrichtet

- **Datenbank**: Elsas Workflow-Definitionen und Laufzeittabellen liegen in der Datenbank der App (Postgres, SQL Server oder SQLite, dieselbe Verbindung wie die App) mit Elsas eigenen Migrationen, die beim Start der API laufen. `Coworkee:Workflows:Elsa:ConnectionString` legt sie woanders ab.
- **API**: Elsas REST-API unter `/api/elsa`, erreichbar über den BFF wie der Rest der API. Nur angemeldete Benutzer mit der Berechtigung `Workflows.Elsa.Manage` kommen durch; Elsas eigene Benutzer und Rollen werden nicht verwendet.
- **Workflows im Code**: `services.AddWorkflow<MyWorkflow>()` registriert einen in C# geschriebenen Workflow.

## Aktivitäten und Trigger

| Aktivität | Kategorie *Coworkee* |
|---|---|
| **Workflow state reached** (Trigger) | startet oder setzt fort, wenn der Workflow einer Entität einen Zustand erreicht, z. B. `Products` erreicht `Approved`; liefert die Id der Entität und den Benutzer |
| **Entity changed** (Trigger) | startet, wenn eine Entität eines beobachteten Typs angelegt, geändert oder gelöscht wird |
| **Wait for approval** | wartet auf die Entscheidung über die Genehmigung der Entität und geht mit *Approved*, *Rejected* oder *Expired* weiter |
| **Run transition** | führt einen Übergang des Entitäts-Workflows als Benutzer aus; die Bedingungen gelten |
| **Send command** | schickt einen beliebigen Befehl oder eine Abfrage der App über den Dispatcher als Benutzer (`RunTransitionCommand`, ein eigenes `CreateOrderCommand`, ...), mit dessen Berechtigungen, Validierung und Unit of Work; das Ergebnis kommt als JSON zurück |
| **Notify** | In-App-Benachrichtigung an Benutzer und die Mitglieder einer Rolle |
| **Query OData** | liest eine OData-Entitätsmenge der App, z. B. `Products` mit `$filter=Rate gt 100` |

*Run as* ist der Benutzer, als der eine Aktivität handelt; leer bleibt es der Benutzer, dessen Aktion den Workflow gestartet hat. Wer Workflows entwerfen darf, kann also als jeder Benutzer handeln, deshalb gehört `Workflows.Elsa.Manage` nur Administratoren.

Entitätstypen, deren Änderungen Workflows starten, werden ausdrücklich freigeschaltet:

```csharp
services.AddCoworkeeElsa(elsa => elsa.Watch<Product>("Products"));
```

Die Trigger werden nach dem Commit über die Outbox versorgt: Übergänge und Entscheidungen (die Domain-Events des Workflow-Moduls) und die Änderungen beobachteter Entitäten, die in derselben Transaktion in die Outbox geschrieben werden. Nichts geht verloren, wenn Elsa beschäftigt ist, und ein scheiternder Workflow rollt die Änderung des Benutzers nicht zurück.

**Query OData** ruft den eigenen OData-Endpunkt der App über HTTP mit einem persönlichen API-Schlüssel eines Dienstbenutzers auf, es gelten also dessen Berechtigungen:

| Einstellung | |
|---|---|
| `Coworkee:Workflows:Elsa:ApiUrl` | die Basisadresse der API, z. B. `https://api.example.com` |
| `Coworkee:Workflows:Elsa:ApiKey` | ein persönlicher API-Schlüssel (in User Secrets oder einem Vault ablegen) |

## Elsa Studio

Elsa Studio, der visuelle Designer, läuft als **eigene App** neben der Blazor-App: `Coworkee.Workflows.Elsa.Studio` macht aus einem kleinen ASP.NET-Projekt einen Host für Elsa Studio (Blazor Server). In die Blazor-App eingebettet ist er bewusst nicht: Studio ist eine vollständige Blazor-Anwendung mit eigenem Router, Layout, MudBlazor-Providern, Radzen, einer Diagramm-Bibliothek und Monaco, und jeder Benutzer der App lüde es herunter, mit oder ohne Berechtigung.

```csharp
// MyApp.ElsaStudio/Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddCoworkeeElsaStudio();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapCoworkeeElsaStudio();
app.Run();
```

Das Projekt ist `Microsoft.NET.Sdk.Web` mit `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>` (es hat keine eigenen `.razor`-Dateien, die Skripte des Frameworks müssen angefordert werden) und dem Paket `Coworkee.Workflows.Elsa.Studio`.

- **Anmeldung**: Der Host meldet sich wie die Web-App am Auth-Server der App an, als eigener OIDC-Client (`Coworkee:Bff`, dieselben Einstellungen wie die Web-App, siehe [Clients and scopes](../security/authentication.md#clients-and-scopes)): dieselben Benutzer, dieselbe Anmeldeseite, Keycloak inklusive. Die Tokens bleiben auf dem Server, der Browser hält nur ein Sitzungscookie.
- **Berechtigung**: Studio ruft die Elsa-API der API (`Coworkee:Workflows:Elsa:Studio:BackendUrl`, z. B. `https://api.example.com/api/elsa`) serverseitig mit dem Access Token des Benutzers auf, die API entscheidet also. Wer `Workflows.Elsa.Manage` nicht hat, sieht statt des Designers eine Seite, die das sagt.
- **Aktivitäten**: Die Palette zeigt die Kategorie *Coworkee* mit den Aktivitäten und Triggern von oben; ihre Ein- und Ausgaben kommen von der API.
- **Link**: Der Navigationseintrag *Workflow designer* der App (Gruppe System, für `Workflows.Elsa.Manage`) führt auf `/api/v1/workflows/elsa/studio`, das auf `Coworkee:Workflows:Elsa:StudioUrl` weiterleitet.

### Im Aspire-App-Host

```csharp
app.AddElsaStudio<Projects.MyApp_ElsaStudio>();   // oder AddProjects(), das *_ElsaStudio findet
```

`AddElsaStudio` registriert den Client `{app}-elsastudio` am Auth-Server (Redirect `/signin-oidc`, API-Scope), gibt dem Host ein eigenes Sitzungscookie und die Adresse der Elsa-API der ersten API und setzt `Coworkee:Workflows:Elsa:StudioUrl` der APIs auf die Adresse des Designers. Ruf es nach der API auf. In einem Deployment ist es ein Werkzeug wie Grafana: `coworkee deploy --without elsastudio` lässt es weg, seine öffentliche Adresse ist der Parameter `<app>-elsastudio-url` (siehe [Deployment](../hosting/deployment.md#tools)).

Ohne laufenden Designer antwortet der Link mit 404; Workflows lassen sich weiter in C# (`WorkflowBase`) schreiben oder als JSON an die API schicken. In C# geschriebene Workflows erscheinen im Designer und lassen sich dort ändern und veröffentlichen; die veröffentlichte Version bleibt nach einem Neustart.

## In einer neuen App

`coworkee new MyApp --workflows --elsa` (oder die Karte *Elsa workflows* im Studio) fügt das Paket der API hinzu; `--elsa-studio` (Karte *Workflow-Designer*) fügt das Projekt `MyApp.ElsaStudio` und seine Zeile im App-Host hinzu. Mit den Beispielen benachrichtigt `MyApp.Api/Workflows/ProductApprovedWorkflow.cs` die Produktmanager, wenn ein Produkt genehmigt ist.
