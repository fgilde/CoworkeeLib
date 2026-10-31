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

Das ist ein ganzer App-Host. `AddProjects()` findet die referenzierten Projekte am Namen (`*_Migrations`, `*_Auth`, `*_Api`, `*_Worker`, `*_Web`, `*_AirWorks`, `*_ElsaStudio`) und verdrahtet jedes in seiner Rolle; eine generierte App ruft stattdessen `AddMigrations`, `AddAuthServer`, `AddApi` und `AddWeb` einzeln auf. So oder so gilt: Die API bekommt ihre Token-Authority, die Web-App ihren OIDC-Client am Auth-Server, und die Migrationen laufen, bevor irgendetwas die Datenbank anfasst. Sharemee hängt seinen Processing-Worker, dessen Name zu keiner Konvention passt, mit einer weiteren Zeile an: `app.AddWorker<Projects.Shareme_Processing_Host>("processing")`.

## Container aus den Paketen

Coworkee liest, welche Pakete ein Dienst verwendet, und startet nur die Container, die er braucht:

| Paket im Dienst | Er bekommt |
|---|---|
| `Coworkee.Infrastructure` | die App-Datenbank; wartet auf die Migrationen |
| `Coworkee.Realtime` | Redis als SignalR-Backplane und für die Cache-Invalidierung (`WithoutRedis()` lässt es weg) |
| `Coworkee.Mailing` | Mailpit mit Posteingang im Browser |
| `Coworkee.Storage` | den gemeinsamen Ordner `.data/blobs` |
| `Coworkee.Search.Elasticsearch` | Elasticsearch |

Standard ist PostgreSQL. `UseSqlServer()` tauscht auf SQL Server, `UseSqlite()` auf eine Datei neben `src`, ganz ohne Container. `UseKeycloak(...)` fügt Keycloak mit einem nach der App benannten Realm und einer Anmeldeseite in ihren Farben hinzu. Alles lässt sich anpassen (`ConfigurePostgres`, `ConfigureRedis`) oder durch eine eigene Ressource ersetzen (`UseDatabase`, `UseRedis`, `UseSearch`).

Optionale Container: `UseLibreTranslate([...])` für die [maschinelle Übersetzung](localization.md); `coworkee new --ai-provider ollama` legt einen Ollama-Container in den App-Host, der das Modell für den [Assistenten](../modules/ai.md) zieht, ohne API-Schlüssel, und `app.AddOpenWebUI(ollama)` stellt [Open WebUI](../hosting/aspire.md#open-webui) dazu, einen Chat über dessen Modelle; `UseKibana()` setzt [Kibana](../hosting/aspire.md#kibana) neben Elasticsearch. Eigene Single-Page-Apps bekommen mit `app.AddSpaClient(...)` einen Client am Auth-Server ([Single-Page-Apps](../hosting/aspire.md#single-page-apps)).

## Grafana und WebDataStudio

![Grafana-Übersichtsdashboard mit Anfragen pro Sekunde, Fehlern, Latenz und Logvolumen je Dienst](../assets/screenshots/grafana-overview-dark.webp){ .shot loading=lazy }

`UseGrafana()` bringt Grafana mit Prometheus, Loki, Tempo und einem OpenTelemetry Collector. Die Dienste melden weiter an das Aspire-Dashboard und schicken dieselbe Telemetrie zusätzlich an den Collector. Bei jedem Start werden sechs Dashboards eingespielt: Overview, .NET runtime, Database access (Abfragen von Entity Framework Core, Connection-Pool, langsamste Spans), PostgreSQL server, App data (Benutzer, Anmeldungen, Outbox, Hintergrundjobs) sowie Logs and traces.

[![WebDataStudio](../assets/brands/webdatastudio.svg){ .cw-product-logo }](https://fgilde.github.io/WebDataStudio/)

`UseWebDataStudio()` öffnet die App-Datenbank im Browser, mit Tabellen, SQL, Diagrammen und Exporten. Bei SQLite hängt es die Datenbankdatei ein.

Beide melden sich mit dem Administrator der App an oder am Auth-Server der App als Administrator der Systemorganisation, ebenso AspireUI, Kibana und Open WebUI.

## AspireUI und die Seite Dienste

[![AspireUI](../assets/brands/aspireui.svg){ .cw-product-logo }](https://fgilde.github.io/AspireUI/)

AspireUI hostet weitere Apps und Stacks neben deiner. Ein Aufruf lässt den Administrator der App hinein, macht die Administratoren der Systemorganisation über den Auth-Server der App zu AspireUI-Admins und gibt AspireUI im Deployment eine öffentliche Adresse:

```csharp
builder.AddAspireUI().WithCoworkee(app).WithRetrieveApi();
```

Jeder Dienst kennt die Adressen der anderen, dazu Aspire- und Hangfire-Dashboard, Mailpit und jeden Container mit Web-Endpunkt. Die Administrationsseite *Dienste* zeigt sie als Kacheln und prüft sie alle zehn Sekunden; mit `WithRetrieveApi()` kommen die Apps von AspireUI dazu.

![Die Administrationsseite Dienste mit einer Kachel je Dienst, seiner Adresse und ob er läuft](../assets/screenshots/services-dark.webp){ .shot loading=lazy }

## Deployment

`coworkee deploy compose|azure|k8s` veröffentlicht den App-Host für Docker Compose, Azure Container Apps oder Kubernetes, mit den Tools des App-Hosts (Keycloak, Grafana, WebDataStudio, LibreTranslate, Ollama, AspireUI, Kibana, Open WebUI) und einer öffentlichen Adresse für jedes; `--without grafana` lässt eines weg. Siehe [Deployment](../hosting/deployment.md).

**Stand:** Docker Compose ist mit einer generierten App geprüft; die Ausgaben für Azure Container Apps und Kubernetes bauen und validieren, eine echte Bereitstellung gab es noch nicht. Für mehr als eine Instanz brauchst du Redis und einen gemeinsamen Data-Protection-Schlüsselring.

Referenz: [Aspire-AppHost](../hosting/aspire.md), [Deployment](../hosting/deployment.md), [Grafana und WebDataStudio](../hosting/observability.md), [Skalieren mit Redis](../hosting/redis.md), [AspireUI](../hosting/aspire.md#aspireui).
