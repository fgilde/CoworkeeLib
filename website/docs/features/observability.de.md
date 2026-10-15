---
title: ".NET Aspire-Hosting mit Grafana und Dev-Tools"
description: "Ein Aufruf von AddCoworkeeApp verbindet im .NET Aspire-App-Host Datenbank, Redis, Mailpit, Elasticsearch und Keycloak, dazu Grafana und ein Datenbank-Browser."
---

# Entwicklerwerkzeuge und Hosting mit .NET Aspire

Bevor eine Geschäftsanwendung irgendetwas Nützliches tut, muss jemand eine Datenbank starten, einen Cache, einen Mailserver für das Zurücksetzen des Passworts, vielleicht eine Suchmaschine und einen Identity Provider, und danach Verbindungszeichenfolgen in vier `appsettings.json`-Dateien kopieren. Wird etwas langsam, stellt sich die nächste Frage: Wo schaut man nach? Logs in der einen Konsole, Traces in der anderen, die Datenbank in einem dritten Werkzeug.

Coworkee baut auf dem .NET Aspire-App-Host auf und übernimmt diese Verdrahtung. Sie benennen die App; Coworkee findet ihre Dienste, liest, welche Pakete jeder Dienst verwendet, und fügt die Container und Einstellungen hinzu, die er braucht. In der Entwicklung kommen Grafana mit Metriken, Logs und Traces sowie ein Datenbankstudio im Browser dazu, beide mit dem Administrator der App angemeldet.

![Grafana-Übersichtsdashboard mit Anfragen pro Sekunde, Fehlern, Latenz und Logvolumen je Dienst](../assets/screenshots/grafana-overview-dark.webp){ .shot loading=lazy }

## Ein Aufruf im Aspire-App-Host

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

`AddProjects()` fügt die Projekte, die der App-Host referenziert, nach Namenskonvention hinzu: `*_Migrations`, `*_Auth`, `*_Api`, `*_Worker`, `*_Web`. Jedes bekommt seine Rolle: Die API erfährt ihre Token-Authority, die Web-App erhält ihren OIDC-Client am Auth-Server, und die Migrationen laufen vor allem, was die Datenbank braucht.

## Infrastruktur aus den Paketen eines Dienstes

Coworkee prüft die Pakete jedes Dienstes, direkt oder über andere Projekte, und fügt nur hinzu, was gebraucht wird:

| Paket im Dienst | Er bekommt |
|---|---|
| `Coworkee.Infrastructure` | die App-Datenbank und wartet, bis die Migrationen fertig sind |
| `Coworkee.Realtime` | Redis als SignalR-Backplane und für die Cache-Invalidierung zwischen Instanzen |
| `Coworkee.Mailing` | Mailpit als lokalen SMTP-Server mit Posteingang im Browser |
| `Coworkee.Storage` | einen gemeinsamen Blob-Ordner `.data/blobs` |
| `Coworkee.Search.Elasticsearch` | Elasticsearch |

Standarddatenbank ist PostgreSQL. `UseSqlServer()` setzt stattdessen einen SQL-Server-Container ein, `UseSqlite()` eine Datei neben `src`, ganz ohne Container; beide stellen den Datenbank-Provider auf allen Diensten ein. `UseKeycloak(...)` fügt Keycloak mit einem nach der App benannten Realm hinzu, mit den angegebenen Benutzern und einer Anmeldeseite in den Farben der App. Jeder Baustein lässt sich konfigurieren (`ConfigurePostgres`, `ConfigureRedis`, ...) oder durch eine eigene Ressource ersetzen (`UseDatabase`, `UseRedis`, `UseSearch`).

## Grafana mit Prometheus, Loki und Tempo

`options.UseGrafana()` fügt Grafana, Prometheus, Loki, Tempo und einen OpenTelemetry Collector hinzu. Die Dienste senden weiter an das Aspire-Dashboard und dieselben Metriken, Logs und Traces zusätzlich an den Collector. Sechs Dashboards werden bei jedem Start bereitgestellt:

- **Overview**: Anfragen pro Sekunde, Fehler, p95-Latenz, SignalR-Verbindungen, meistgenutzte und langsamste Endpunkte
- **.NET runtime**: CPU, Speicher, GC, Thread-Pool, Exceptions nach Typ
- **Database access**: Abfragen und SaveChanges von Entity Framework Core, Connection-Pool, langsamste Datenbank-Spans
- **PostgreSQL server**: Verbindungen, Cache-Trefferquote, Deadlocks (nur Postgres)
- **App data**: Benutzer, Anmeldungen, Mandanten, Outbox und Hintergrundjobs
- **Logs and traces**: alle Logzeilen mit Filtern, Traces mit Verweis auf ihre Logs

Jedes Dashboard filtert nach Dienst und verweist auf die anderen.

## WebDataStudio: die Datenbank im Browser

[![WebDataStudio](../assets/brands/webdatastudio.svg){ .cw-product-logo }](https://fgilde.github.io/WebDataStudio/)

`options.UseWebDataStudio()` fügt ein Datenbankstudio mit fertiger Verbindung zur App-Datenbank hinzu: Tabellen, SQL, Diagramme und Exporte. Bei SQLite öffnet es die Datenbankdatei. Wie Grafana meldet es sich mit E-Mail und Passwort des Administrators an, mit Keycloak zusätzlich über die Keycloak-Schaltfläche. Beide Werkzeuge kommen nur im Run-Modus hinzu; `aspire publish` lässt sie weg.

![WebDataStudio mit einer Tabelle der App-Datenbank](../assets/screenshots/webdatastudio-dark.webp){ .shot loading=lazy }

## AspireUI und die Seite Dienste

[![AspireUI](../assets/brands/aspireui.svg){ .cw-product-logo }](https://fgilde.github.io/AspireUI/)

AspireUI hostet weitere Apps und Stacks neben Ihrer App. Mit Keycloak genügt ein Aufruf, damit sich die Administratoren der App dort als Admins anmelden:

```csharp
builder.AddAspireUI().WithCoworkeeSignOn(app).WithRetrieveApi();
```

Jeder Dienst kennt die Adressen der anderen, des Aspire-Dashboards, des Hangfire-Dashboards, von Mailpit und jedes Containers mit Web-Endpunkt. Die Administrationsseite *Dienste* zeigt sie als Kacheln mit ihrem aktuellen Zustand, der alle zehn Sekunden geprüft wird; mit `WithRetrieveApi()` erscheinen dort auch die Apps von AspireUI.

![Die Administrationsseite Dienste mit einer Kachel je Dienst, seiner Adresse und ob er läuft](../assets/screenshots/services-dark.webp){ .shot loading=lazy }

## Container für LibreTranslate und Ollama

Zwei weitere Container sind eine Option entfernt. `UseLibreTranslate([...])` startet einen LibreTranslate-Server für die [maschinelle Übersetzung](localization.md) mit genau den benötigten Sprachmodellen, abgelegt in einem Volume, das sich alle Apps auf dem Rechner teilen. `coworkee new --ai-provider ollama` fügt einen Ollama-Container hinzu, der das Modell für den [KI-Assistenten](../modules/ai.md) lädt; die App läuft dann offline und ohne Schlüssel.

## Weiterlesen

- [Aspire-App-Host](../hosting/aspire.md): Rollen, Konventionen, eigene Infrastruktur, Keycloak und Veröffentlichung nach Docker Compose oder Azure
- [Grafana und WebDataStudio](../hosting/observability.md): alle Ressourcen und Dashboards, Anmeldung, Ein- und Ausschalten
- [Skalieren mit Redis](../hosting/redis.md): was Redis zwischen Instanzen trägt
- [Dienstadressen](../hosting/aspire.md#dienstadressen) und die [Seite Dienste](../modules/settings.md#dienste)
- [AspireUI](../hosting/aspire.md#aspireui): Single Sign-on über den Keycloak-Realm der App
