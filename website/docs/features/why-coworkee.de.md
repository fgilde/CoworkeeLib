---
title: "Warum ein Anwendungsframework für .NET?"
description: "Warum auf Coworkee bauen: aktualisierbare NuGet-Pakete, Module nach Bedarf, Sicherheit, Mandanten und Observability eingebaut, eine Basis für KI-Agenten, CLI und Studio, auf einem offenen .NET-10-Stack."
---

# Warum Coworkee

Jede Business-App braucht vor dem ersten Feature dasselbe Fundament: Anmeldung, Benutzer und Rollen, Berechtigungen, Mandanten, Einstellungen, eine Admin-Oberfläche, Audit, Mailversand, eine lokale Umgebung und einen Weg ins Deployment. Coworkee liefert dieses Fundament als NuGet-Pakete für ASP.NET Core, Blazor und Aspire. Der Code in deiner Solution ist deine Fachlichkeit.

## Pakete, die du aktualisierst, statt Code, den du erbst

Alles kommt als Pakete mit dem Präfix `Coworkee.`, festgelegt durch ein einziges `<CoworkeeVersion>` in `Directory.Packages.props`. Ein Fix in der Anmeldung, eine neue Sprache oder eine schnellere Datentabelle erreicht jede App mit der nächsten Version.

`coworkee update` setzt diese Version und wendet die Upgrade-Regeln an, die das CLI für jedes Release mitbringt: Es zeigt die Breaking Changes zwischen deiner und der neuen Version, erledigt, was sich automatisch erledigen lässt (Umbenennungen, Paket- und SDK-Versionen, Zeilen aus der Vorlage), und listet die Handgriffe mit jeder betroffenen `Datei:Zeile`. `--dry-run` zeigt den Plan und ändert nichts. Das [Änderungsprotokoll](../reference/changelog.md) führt dieselben Änderungen pro Version auf.

## Modular: nimm, was du brauchst

Jeder Bereich ist ein Modul in einem eigenen Paket: Identity, Einstellungen, Mailversand, Dateien, Suche, Realtime, Benachrichtigungen, Audit, Workflows, Berichte, der KI-Assistent, Chat, Webhooks, Backups und mehr. Lässt du ein Modul weg, bleiben seine Tabellen, Endpunkte und Seiten draußen. `coworkee add` und `coworkee remove` schalten Optionen einer bestehenden App später um, und Coworkee Studio startet mit Vorlagen (minimal, Business-App, Zusammenarbeit, SaaS-Plattform und weitere), mit Vor- und Nachteilen jeder Wahl daneben. Siehe [Modulare Architektur](architecture.md).

## Produktionsthemen eingebaut

- **Sicherheit.** Ein OpenIddict-Auth-Server mit Keycloak oder einem beliebigen OpenID-Connect-Anbieter, Tokens auf dem Server hinter einem Sitzungscookie, Berechtigungen bis auf die einzelne Ressource, persönliche [API-Schlüssel](../security/api-keys.md), Rate-Limits und eine Content-Security-Policy auf den Anmeldeseiten und in der Web-App. Siehe [Identity und Sicherheit](identity.md).
- **Mandanten.** `IMultiTenant` an einer Entity filtert jede Abfrage nach Organisation; Features und Editionen machen aus einer App ein SaaS-Produkt. Siehe [Mandanten](multi-tenancy.md).
- **Audit und Verlauf.** Audit auf Feldebene in derselben Transaktion wie die Änderung, Verlauf eines Datensatzes mit Wiederherstellen und Rückgängig. Die [Aufbewahrung](../modules/retention.md) löscht Audit-Einträge, Logs, Tokens und gelöschte Datensätze nach den Tagen, die du festlegst, und [Datenbank-Backups](../modules/backups.md) werden in der Administration erstellt, heruntergeladen, hochgeladen und wiederhergestellt.
- **Observability.** Ein [Aspire-AppHost](observability.md) startet die App mit PostgreSQL oder SQL Server, Redis, Mailpit und auf Wunsch Grafana mit Prometheus, Loki und Tempo. `coworkee deploy` veröffentlicht denselben AppHost für Docker Compose, Azure Container Apps oder Kubernetes.
- **Tests.** `Coworkee.Testing` führt API-Tests gegen eine echte PostgreSQL-, SQL-Server- oder SQLite-Datenbank aus und handelt als beliebiger Benutzer; die erzeugte Solution bringt Testprojekte für API, AppHost, Migrationen und Blazor-Client mit. Die Bibliothek selbst hat mehr als 2.000 Tests; die CI führt die datenbanklastigen Suiten auf allen drei Datenbanken aus. Siehe [Tests](../testing.md).

## Eine Basis, um mit KI zu bauen

Weil Sicherheit, Mandanten, Audit, Realtime und Admin-Oberfläche mit den Paketen kommen, muss ein Coding-Agent nur die Fachlichkeit schreiben, und sein Code steckt in einem Rahmen, der Berechtigungen schon prüft und nach Mandant filtert. Das CLI ist ein MCP-Server (`coworkee mcp`), eine ganze App ist eine JSON-Spec mit veröffentlichtem Schema, und jede laufende App ist ein MCP-Server für ihre eigenen Daten. Siehe [Mit KI bauen](building-with-ai.md).

## Werkzeuge

- **CLI.** `coworkee new`, `run`, `module add`, `entity add`, `entity import` (aus einer Datenbank, OpenAPI oder CSV), `migrations add`, `update`, `deploy`, `doctor`. Siehe [Kommandozeile](../getting-started/cli.md).
- **Coworkee Studio.** Ein Desktop-Assistent für Windows, macOS und Linux: Name, Logo und Theme, Anmeldedesign, Module und Infrastruktur, Entities, dann legt er die App an und startet sie. Siehe [Coworkee Studio](../getting-started/studio.md).
- **AirWorks-Client.** `coworkee new --airworks` stellt neben die Blazor-App einen Web-Desktop mit Fenstern und Taskleiste, am selben Auth-Server und derselben API. Siehe [AirWorks-Client](../web/airworks.md).

## Ein offener .NET-10-Stack

.NET 10, Minimal APIs von ASP.NET Core, Blazor mit MudBlazor und MudBlazor.Extensions, EF Core auf PostgreSQL, SQL Server oder SQLite, OpenIddict, FluentValidation und Aspire. MIT-Lizenz, der Quellcode liegt auf [GitHub](https://github.com/fgilde/CoworkeeLib).

Dein eigener Code bleibt schlicht: Endpunkte sind Minimal APIs, ein handgeschriebener neben den erzeugten ist kein Problem. Jede Blazor-Komponente der Bibliothek lässt sich über [`ReplaceComponent`](../ui/customizing.md) austauschen, und Mailversand, maschinelle Übersetzung, Blob-Speicher und Suche stecken hinter Schnittstellen, die du selbst implementieren kannst.

## Gut zu wissen

- Coworkee setzt .NET 10 voraus.
- Der Stack ist festgelegt: Eine andere UI-Bibliothek oder ein anderes ORM heißt, gegen das Framework zu arbeiten.
- Die Pakete stehen auf 0.0.1.x, die API bewegt sich noch zwischen Releases. Leg die Version fest und lass `coworkee update --dry-run` laufen, bevor du aktualisierst.
- Die Konventionen brauchen etwas Einarbeitung. [Das erste Feature](../getting-started/first-feature.md) zeigt sie an einer Entity.

```bash
dotnet tool install -g Coworkee.Cli
coworkee new Shop
cd Shop && coworkee run
```

Oder `coworkee ui` für den Desktop-Assistenten. Die [Einsatzbeispiele](use-cases.md) zeigen, wofür das Framework gemacht ist.
