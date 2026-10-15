---
title: "Features des Anwendungsframeworks für .NET"
description: "Was Coworkee einer Blazor- und ASP.NET-Core-App mitbringt: Module, Identity, Mandanten, Daten, Admin-Oberfläche, Realtime, KI, Berichte, Sprachen und Aspire."
---

# Features

Anmeldung, Benutzerliste, Rollen, Einstellungsseite, Audit-Log, eine Tabelle mit Filtern. Coworkee liefert das als NuGet-Pakete für ASP.NET Core und Blazor, und ein Aspire-AppHost startet alles zusammen.

Die Seiten unten gehen Bereich für Bereich durch, mit Code aus den echten APIs und Screenshots aus der Demo-App (Katalog, Dokumente und 100.000 Aufträge).

<div class="grid cards" markdown>

-   **[Modulare Architektur](architecture.md)**

    Module mit `[DependsOn]`, eine Request-Pipeline mit Validierung, Berechtigungen und Caching, das CLI `coworkee` und Coworkee Studio.

-   **[Identity und Sicherheit](identity.md)**

    Benutzer, Rollen, Gruppen, Berechtigungen bis auf einen einzelnen Datensatz, ein OpenIddict-Auth-Server, Keycloak oder jeder OpenID-Connect-Anbieter, persönliche API-Schlüssel.

-   **[Mandanten und Einstellungen](multi-tenancy.md)**

    Organisationen mit getrennten Daten, Einstellungen für Host, Organisation und Benutzer, Features und Editionen für ein SaaS-Produkt.

-   **[Daten und Abfragen](data.md)**

    EF Core auf PostgreSQL, SQL Server oder SQLite, Audit auf Feldebene, OData mit Facetten, ein Query-Builder und Client-Entities, die im Browser antworten.

-   **[Blazor-Admin-Oberfläche](blazor-ui.md)**

    Eine MudBlazor-Shell mit Navigation, Admin-Seiten, Datentabellen, Formularen aus dem Modell, Themes und drei Anmelde-Designs.

-   **[Realtime und Zusammenarbeit](realtime.md)**

    Live-Änderungen an Entities, Benachrichtigungen, Präsenz, Chat, Kommentare und Bewertungen, Mail-Kampagnen und Hintergrundjobs.

-   **[KI-Assistent und MCP](ai.md)**

    Ein Assistent, der mit den Rechten des Benutzers arbeitet, 14 Modellanbieter und ein MCP-Server in jeder App.

-   **[Berichte und Dashboards](reporting.md)**

    Dashboards, im Browser auf deinen OData-Sets gebaut und von der Datenbank in Millisekunden aggregiert.

-   **[Lokalisierung](localization.md)**

    23 mitgelieferte Sprachen, Texte zur Laufzeit bearbeiten und maschinell übersetzen mit LibreTranslate, DeepL und anderen.

-   **[Aspire und Observability](observability.md)**

    Ein AppHost für Datenbank, Redis, Keycloak, Grafana mit Logs, Metriken und Traces, WebDataStudio und AspireUI.

-   **[Dateien und Suche](files.md)**

    Eine Dateiablage mit Vorschau und öffentlichen Links auf Blob-Storage, dazu Volltextsuche.

-   **[Einsatzbeispiele](use-cases.md)**

    Backoffice, B2B-SaaS, Kundenportal und Sharemee, die Asset-Plattform, für die das Framework herausgelöst wurde.

</div>

Lohnt sich ein Framework überhaupt, verglichen mit einer schlichten ASP.NET-Core-Vorlage? [Warum Coworkee](why-coworkee.md) stellt beides nebeneinander, auch da, wo die Vorlage gewinnt.
