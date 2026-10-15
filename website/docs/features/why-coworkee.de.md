---
title: "Warum ein Anwendungsframework für .NET?"
description: "Coworkee im Vergleich mit einem selbst gebauten ASP.NET-Core-Projekt und einer kopierten Projektvorlage: was Sie selbst bauen, was Sie pflegen, was es kostet."
---

# Warum Coworkee: Framework, Vorlage oder bei null anfangen

Für eine Geschäftsanwendung auf ASP.NET Core und Blazor gibt es drei übliche Starts. Sie schreiben die gemeinsamen Teile selbst. Sie kopieren eine Projektvorlage, die sie schon enthält. Oder Sie nehmen sie als Pakete aus einem Framework und behalten in der Solution nur Ihre Fachlichkeit. Coworkee ist der dritte Weg und ist aus dem zweiten entstanden: Es begann als meine eigene Blazor-Vorlage, die ich in jede neue App kopiert habe, bis das Abgleichen der Kopien mehr kostete als die Apps selbst.

Diese Seite vergleicht die drei Wege, ohne so zu tun, als hätte das Framework keinen Preis.

## Nebeneinander

| | Selbst gebautes ASP.NET Core | Kopierte Projektvorlage | Coworkee-Pakete |
|---|---|---|---|
| Anmeldung, Benutzer, Rollen, Berechtigungen | Sie schreiben und sichern es | in der Kopie, eingefroren am Tag des Kopierens | [Pakete](identity.md), aktualisiert per Versionssprung |
| Auth-Server und externe Anbieter | Sie binden OpenIddict, Keycloak oder einen anderen IdP an | oft nur einer davon | OpenIddict-Auth-Server plus Keycloak oder jeder OIDC-Anbieter, Anmelde-Designs inklusive |
| Mandanten | Abfragefilter und Mandantenerkennung von Hand | hängt von der Vorlage ab | [Mandanten, Features und Editionen](multi-tenancy.md) |
| Admin-Oberfläche | Sie bauen jede Seite | Seiten in der Kopie, von Ihnen zu pflegen | Benutzer, Rollen, Einstellungen, Themes, Sprachen, Mail, Audit, Backups, KI und mehr fertig dabei |
| Datentabellen mit Filtern und Export | pro Entity oder eine Grid-Bibliothek, die Sie verdrahten | eine generische Tabelle, wenn überhaupt | [Facetten, Spaltenfilter, Gruppierung, Ansichten, Export und Import](blazor-ui.md) über OData für jede Entity |
| Audit-Trail | Interceptoren, die Sie schreiben | vielleicht | [auf Feldebene, mit Zeitleiste pro Datensatz](data.md) |
| Realtime | SignalR-Hubs, die Sie entwerfen | selten | [Entity-Änderungen, Benachrichtigungen, Präsenz, Chat](realtime.md) |
| Lokale Umgebung | Docker-Compose-Dateien, die Sie aktuell halten | was beim Kopieren dabei war | ein [Aspire-AppHost](observability.md) mit Datenbank, Redis, Mailpit, Keycloak, Grafana |
| Sicherheitskorrekturen und Verbesserungen | Ihre Sache | von Hand in jede Kopie | `dotnet add package` bzw. ein Versionssprung |
| Zeit bis zum ersten fachlichen Feature | Wochen | Tage, danach Auseinanderdriften | `coworkee new` und eine Entity |

## Was Sie gewinnen

**Weniger Code, der nicht Ihr Produkt ist.** In der Demo-App besteht der Code der Solution aus Katalog, Dokumenten und Aufträgen. Alles andere auf den Screenshots kommt aus Paketen. Dasselbe gilt für Sharemee, eine Plattform für digitale Assets: Ihre eigenen Projekte handeln von Assets, Metadaten und Verarbeitung.

**Updates statt Auseinanderdriften.** Eine Vorlage ist ein Schnappschuss. Eine Korrektur im Anmeldeablauf, eine neue Sprache oder eine schnellere Abfrage in der Datentabelle erreicht jede App auf Coworkee mit der nächsten Paketversion. Breaking Changes stehen im [Änderungsprotokoll](../reference/changelog.md).

**Entscheidungen, die schon getroffen und getestet sind.** Rund 1.500 automatisierte Tests laufen gegen PostgreSQL, SQL Server und SQLite. Die Template-Tests des CLI bauen echte generierte Apps. Ihre fachlichen Tests bleiben Ihre, die für Passwortregeln, Berechtigungsprüfungen oder Mandantenfilter nicht.

**Ein Weg, Dinge zu tun.** Jeder Request läuft durch dieselbe Pipeline mit Validierung, Berechtigungsprüfung, Caching und Unit of Work. Neue Entwickler lernen sie einmal und finden sie in jedem Modul wieder.

## Was es kostet

**Coworkee hat eine Meinung.** Es nutzt EF Core, MudBlazor und MudBlazor.Extensions, OpenIddict, FluentValidation und Aspire. Hat sich Ihr Team auf eine andere UI-Bibliothek oder ein anderes ORM festgelegt, arbeiten Sie gegen das Framework statt mit ihm.

**Es setzt .NET 10 voraus.** Apps, die auf einer älteren Laufzeit bleiben müssen, können es nicht nutzen.

**Sie lernen seine Konventionen.** Module, `[DependsOn]`, der Dispatcher und das Einstellungsmodell wollen verstanden sein, bevor Sie schnell werden. Die Anleitung [Das erste Feature](../getting-started/first-feature.md) zeigt sie alle an einer Entity.

**Es ist ein junges Projekt.** Die Pakete stehen auf Version 0.0.1.x, und die API bewegt sich noch zwischen Releases. Pinnen Sie Versionen und lesen Sie vor einem Update das Änderungsprotokoll.

## Notausgänge

Ein Framework ist nur annehmbar, wenn Sie dort an ihm vorbeikommen, wo es nicht passt:

- Jede Blazor-Komponente der Bibliothek lässt sich über `ReplaceComponent` durch eine eigene [ersetzen](../ui/customizing.md).
- Module sind optional. Lassen Sie Chat, Berichte oder KI weg, bleiben auch deren Tabellen, Endpunkte und Seiten draußen.
- Ihre Endpunkte sind normale Minimal-APIs von ASP.NET Core; nichts hindert Sie, neben den generierten einen von Hand zu schreiben.
- Mail-Transporte, Übersetzungsanbieter, Blob-Storage und Suche stecken hinter Schnittstellen, die Sie selbst implementieren können.

## Ausprobieren

```bash
dotnet tool install -g Coworkee.Cli
coworkee new Shop
cd Shop && coworkee run
```

Oder starten Sie mit `coworkee ui` den Desktop-Assistenten. Die [Installationsanleitung](../getting-started/installation.md) erklärt jeden Schritt, und die [Einsatzbeispiele](use-cases.md) zeigen, für welche Apps das Framework gedacht ist.
