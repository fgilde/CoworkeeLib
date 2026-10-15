---
title: "Warum ein Anwendungsframework für .NET?"
description: "Coworkee im Vergleich mit einem selbst gebauten ASP.NET-Core-Projekt und einer kopierten Projektvorlage: was du selbst baust, was du pflegst und wo welcher Weg gewinnt."
---

# Warum Coworkee

Die gemeinsamen Teile einer App mit ASP.NET Core und Blazor kannst du selbst schreiben, aus einer Projektvorlage kopieren oder als Pakete einbinden. Coworkee ist der dritte Weg und aus dem zweiten entstanden: Am Anfang stand meine eigene Blazor-Vorlage, die ich in jede neue App kopiert habe, bis mich das Abgleichen der Kopien mehr Zeit gekostet hat als die Apps selbst.

## Im Vergleich

| | Selbst gebaut mit ASP.NET Core | Kopierte Projektvorlage | Coworkee-Pakete |
|---|---|---|---|
| Anmeldung, Benutzer, Rollen, Berechtigungen | schreibst und sicherst du selbst | in der Kopie, Stand vom Tag des Kopierens | [Pakete](identity.md), aktuell mit der Paketversion |
| Mandanten | Abfragefilter und Mandantenerkennung von Hand | je nach Vorlage | [Mandanten, Features und Editionen](multi-tenancy.md) |
| Admin-Seiten | baust du alle selbst | in der Kopie, pflegst du weiter | Benutzer, Rollen, Einstellungen, Themes, Sprachen, Mail, Audit, Backups und mehr |
| Tabellen mit Filtern und Export | pro Entity | eine generische Tabelle, wenn überhaupt | [Facetten, Spaltenfilter, Gruppierung, Ansichten, Export](blazor-ui.md) über OData |
| Lokale Umgebung | Compose-Dateien, die du aktuell hältst | was beim Kopieren dabei war | ein [Aspire-AppHost](observability.md) |
| Korrekturen im gemeinsamen Code | deine Sache | von Hand in jede Kopie | mit der nächsten Paketversion |
| Kontrolle über jede Zeile | vollständig | vollständig, nach dem Kopieren | über [austauschbare Komponenten](../ui/customizing.md) und Schnittstellen, nicht überall |
| Einarbeitung | nichts über ASP.NET Core hinaus | die Konventionen der Vorlage | Module, Dispatcher, Einstellungsmodell |
| Laufzeit | jede, auch .NET 8 LTS | was die Vorlage vorgibt | nur .NET 10 |
| Abhängigkeit | keine | nach dem Kopieren keine | ein 0.0.x-Projekt mit einem Maintainer |

Die letzten vier Zeilen gehen an das selbst gebaute Projekt, und das ist auch so gemeint. Wenn du auf .NET 8 LTS angewiesen bist oder dein Team keine Zeit für noch ein Regelwerk hat, fang bei null an.

## Was du dafür bekommst

Am besten sieht man es an der Demo-App. Ihr eigener Code besteht aus Produktkatalog, Dokumenten und Aufträgen. Alles andere auf den Screenshots dieser Seite, von der Anmeldung bis zum Audit-Log, kommt aus den Paketen. Bei Sharemee, der zweiten App auf der Bibliothek, ist es genauso: Sharemees eigener Code dreht sich um Assets und Metadaten, nicht um Benutzerverwaltung.

Eine Vorlage ist ein Schnappschuss. Wenn ich die Anmeldung repariere, eine Sprache ergänze oder die Datentabelle schneller mache, bekommt das jede App auf Coworkee mit der nächsten Version. Was du dabei anpassen musst, steht im [Änderungsprotokoll](../reference/changelog.md). Rund 1.500 automatisierte Tests laufen gegen PostgreSQL, SQL Server und SQLite, und die Template-Tests des CLI bauen echte generierte Apps.

Jeder Request läuft durch dieselbe Pipeline (Validierung, Berechtigungsprüfung, Caching, Unit of Work). Wer sie in einem Modul gesehen hat, kennt sie in allen.

## Was es kostet

- Coworkee legt sich fest: EF Core, MudBlazor mit MudBlazor.Extensions, OpenIddict, FluentValidation, Aspire. Mit einer anderen UI-Bibliothek oder einem anderen ORM arbeitest du gegen das Framework.
- Nur .NET 10.
- Konventionen, die man lernen muss. [Das erste Feature](../getting-started/first-feature.md) zeigt sie an einer Entity.
- Eine junge API. Die Pakete stehen bei 0.0.1.x und ändern sich noch zwischen den Releases; pinne die Versionen und lies vor jedem Update das Änderungsprotokoll.

## Wo du drumherum kommst

Jede Blazor-Komponente der Bibliothek lässt sich über `ReplaceComponent` austauschen. Module sind optional: Lässt du Chat oder Berichte weg, bleiben auch ihre Tabellen, Endpunkte und Seiten draußen. Deine Endpunkte sind normale Minimal-APIs, ein handgeschriebener neben den generierten ist also kein Problem. Mail-Versand, maschinelle Übersetzung, Blob-Storage und Suche hängen an Schnittstellen, die du selbst implementieren kannst.

```bash
dotnet tool install -g Coworkee.Cli
coworkee new Shop
cd Shop && coworkee run
```

Oder `coworkee ui` für den Desktop-Assistenten. Die [Einsatzbeispiele](use-cases.md) zeigen, wofür das Framework gedacht ist.
