---
title: ".NET Aspire-Hosting mit Grafana und Dev-Tools"
description: "Ein Aufruf von AddCoworkeeApp verbindet im .NET Aspire-App-Host Datenbank, Redis, Mailpit, Elasticsearch und Keycloak, dazu Grafana und ein Datenbank-Browser."
---

# Hosting und Entwicklerwerkzeuge

```csharp title="MyApp.AppHost/AppHost.cs"
var builder = DistributedApplication.CreateBuilder(args);

builder.AddCoworkeeApp("myapp", options =>
{
    options.DisplayName = "MyApp";
    options.UseGrafana();
    options.UseWebDataStudio();
}).AddProjects();

builder.Build().Run();
```

So sieht der Kern des App-Hosts einer generierten App aus. `AddProjects()` findet die referenzierten Projekte am Namen (`*_Migrations`, `*_Auth`, `*_Api`, `*_Worker`, `*_Web`) und verdrahtet jedes in seiner Rolle: Die API bekommt ihre Token-Authority, die Web-App ihren OIDC-Client am Auth-Server, und die Migrationen laufen, bevor irgendetwas die Datenbank anfasst. Sharemee hängt seinen Processing-Worker, dessen Name zu keiner Konvention passt, mit einer weiteren Zeile an: `app.AddWorker<Projects.Shareme_Processing_Host>("processing")`.

## Container aus den Paketen

Coworkee liest, welche Pakete ein Dienst verwendet, und startet nur die Container, die er braucht:

| Paket im Dienst | Er bekommt |
|---|---|
| `Coworkee.Infrastructure` | die App-Datenbank; wartet auf die Migrationen |
| `Coworkee.Realtime` | Redis als SignalR-Backplane und für die Cache-Invalidierung |
| `Coworkee.Mailing` | Mailpit mit Posteingang im Browser |
| `Coworkee.Storage` | den gemeinsamen Ordner `.data/blobs` |
| `Coworkee.Search.Elasticsearch` | Elasticsearch |

Standard ist PostgreSQL. `UseSqlServer()` tauscht auf SQL Server, `UseSqlite()` auf eine Datei neben `src`, ganz ohne Container. `UseKeycloak(...)` fügt Keycloak mit einem nach der App benannten Realm und einer Anmeldeseite in ihren Farben hinzu. Alles lässt sich anpassen (`ConfigurePostgres`, `ConfigureRedis`) oder durch eine eigene Ressource ersetzen (`UseDatabase`, `UseRedis`, `UseSearch`).

Zwei Container gibt es optional dazu: `UseLibreTranslate([...])` für die [maschinelle Übersetzung](localization.md), und `coworkee new --ai-provider ollama` legt einen Ollama-Container in den App-Host, der das Modell für den [Assistenten](../modules/ai.md) zieht. Ein API-Schlüssel ist dafür nicht nötig.

## Grafana und WebDataStudio

![Grafana-Übersichtsdashboard mit Anfragen pro Sekunde, Fehlern, Latenz und Logvolumen je Dienst](../assets/screenshots/grafana-overview-dark.webp){ .shot loading=lazy }

`UseGrafana()` bringt Grafana mit Prometheus, Loki, Tempo und einem OpenTelemetry Collector. Die Dienste melden weiter an das Aspire-Dashboard und schicken dieselbe Telemetrie zusätzlich an den Collector. Bei jedem Start werden sechs Dashboards eingespielt: Overview, .NET runtime, Database access (Abfragen von Entity Framework Core, Connection-Pool, langsamste Spans), PostgreSQL server, App data (Benutzer, Anmeldungen, Outbox, Hintergrundjobs) sowie Logs and traces.

[![WebDataStudio](../assets/brands/webdatastudio.svg){ .cw-product-logo }](https://fgilde.github.io/WebDataStudio/)

`UseWebDataStudio()` öffnet die App-Datenbank im Browser, mit Tabellen, SQL, Diagrammen und Exporten. Bei SQLite hängt es die Datenbankdatei ein.

Beide melden sich mit dem Administrator der App an, mit Keycloak zusätzlich über den Keycloak-Button. In `aspire publish` landet keines von beiden, und das ist Absicht: Ein Deployment sollte kein Datenbankstudio mit dem Admin-Passwort ausliefern.

## AspireUI und die Seite Dienste

[![AspireUI](../assets/brands/aspireui.svg){ .cw-product-logo }](https://fgilde.github.io/AspireUI/)

AspireUI hostet weitere Apps und Stacks neben deiner. Mit Keycloak werden die Administratoren der App über einen einzigen Aufruf zu AspireUI-Admins:

```csharp
builder.AddAspireUI().WithCoworkeeSignOn(app).WithRetrieveApi();
```

Jeder Dienst kennt die Adressen der anderen, dazu Aspire- und Hangfire-Dashboard, Mailpit und jeden Container mit Web-Endpunkt. Die Administrationsseite *Dienste* zeigt sie als Kacheln und prüft sie alle zehn Sekunden; mit `WithRetrieveApi()` kommen die Apps von AspireUI dazu.

![Die Administrationsseite Dienste mit einer Kachel je Dienst, seiner Adresse und ob er läuft](../assets/screenshots/services-dark.webp){ .shot loading=lazy }

**Stand:** Das Veröffentlichen mit `aspire publish` nach Docker Compose oder Azure Container Apps ist verdrahtet, aber noch nicht durchgängig geprüft. Für mehr als eine Instanz brauchst du Redis und einen gemeinsamen Data-Protection-Schlüsselring.

Referenz: [Aspire-AppHost](../hosting/aspire.md), [Grafana und WebDataStudio](../hosting/observability.md), [Skalieren mit Redis](../hosting/redis.md), [AspireUI](../hosting/aspire.md#aspireui).
