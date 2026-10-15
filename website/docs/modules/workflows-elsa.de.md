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

Elsa Studio, der visuelle Designer, ist **nicht** in die Blazor-App eingebettet. Es ist eine vollständige eigene Blazor-Anwendung: eigener Router, eigenes Layout und eigene MudBlazor-Provider, Radzen, eine Diagramm-Bibliothek in JavaScript und Monaco, dazu Module, die sich beim Start initialisieren und die Root-Komponente für sich beanspruchen. In der WebAssembly-App von Coworkee würde es mit den MudBlazor-Providern und dem Layout der App konkurrieren, und jeder Benutzer lüde die mehreren Megabyte des Designers herunter, mit oder ohne Berechtigung.

Die Bibliothek bringt dafür noch keinen Host mit. Betreib Studio stattdessen als eigene kleine Blazor-WebAssembly-App (Elsas `Elsa.Studio`-Pakete, siehe die Elsa-Dokumentation) unter dem Origin der Web-App, damit es mit der Sitzung des Benutzers über den BFF mit Elsa spricht:

1. Hoste Studio z. B. unter `/elsa-studio/` und richte sein Backend auf `/api/elsa` der Web-App. Anfragen tragen dann das Sitzungscookie des Benutzers durch den BFF.
2. Gib dem Backend-Client von Studio (`BackendApiConfig.ConfigureHttpClientBuilder`) einen HTTP-Handler, der den Header `X-CSRF: 1` mitschickt, den der BFF bei jeder ändernden Anfrage erwartet.
3. Liefere Studio unter dem Origin der Web-App aus: vom Web-Host selbst als zweite WebAssembly-App mit dem Pfad `/elsa-studio`, oder hinter einem gemeinsamen Reverse Proxy mit der Web-App.
4. Setz `Coworkee:Workflows:Elsa:StudioUrl` auf `/elsa-studio/`: Der Navigationseintrag *Workflow designer* (Gruppe System, für `Workflows.Elsa.Manage`) führt dorthin.

Ohne Studio schreibst du Workflows in C# (`WorkflowBase`) oder als JSON über die API.

## In einer neuen App

`coworkee new MyApp --workflows --elsa` (oder die Karte *Elsa workflows* im Studio) fügt das Paket der API hinzu. Mit den Beispielen benachrichtigt `MyApp.Api/Workflows/ProductApprovedWorkflow.cs` die Produktmanager, wenn ein Produkt genehmigt ist.
