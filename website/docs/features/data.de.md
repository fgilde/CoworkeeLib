---
title: "EF-Core-Datenschicht und OData für .NET-Anwendungen"
description: "Die EF-Core-Datenschicht von Coworkee: PostgreSQL, SQL Server oder SQLite, Audit pro Feld, Outbox, OData-Sets mit Facetten, Abfragen im Browser, Excel-Import."
---

# EF-Core-Datenschicht mit OData, Audit und Abfragen im Browser

Ein großer Teil des Codes einer Geschäftsanwendung ist Infrastruktur rund um die Datenbank: Listen-Endpunkte mit Blättern und Filtern, eine Audit-Tabelle, die irgendwer zu füllen vergessen hat, Soft-Delete-Flags, die in jeder zweiten Abfrage auftauchen, ein Export-Knopf, der den Filter des Benutzers ignoriert. Coworkee erledigt das einmal, in der Bibliothek, und der Code der App besteht aus dem Domänenmodell und den Commands, die es ändern.

Die Datenschicht ist schlichtes EF Core. Es gibt keine Repositories und keine eigene Abfragesprache: Handler injizieren `CoworkeeDbContext` und rufen `db.Set<T>()` auf. Was die Bibliothek ergänzt, liegt um diesen Kontext herum: Konventionen für Mandanten, Soft Delete und Audit, eine Outbox und der Weg, jede Entity als filterbares OData-Set bereitzustellen.

![Tabelle der Aufträge mit 100.000 Zeilen und den gemessenen Medianzeiten von Server und Browser](../assets/screenshots/orders-compare-light.webp){ .shot loading=lazy }

## Ein DbContext für den ganzen modularen Monolithen

Die App leitet einen `DbContext` von `CoworkeeDbContext` ab. Module steuern ihre Entities bei, die Bibliothek die Tabellen für Identität, Einstellungen, Audit und Outbox.

```csharp
public sealed class MyAppDbContext(DbContextOptions<MyAppDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

context.Services.AddCoworkeeDbContext<MyAppDbContext>((provider, options) =>
    options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "myapp"));
```

Das Verhalten ergibt sich aus Basisklassen und Markern an der Entity:

| Marker | Wirkung |
|---|---|
| `AuditedEntity` | `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy` werden beim Speichern gesetzt |
| `IMultiTenant` | Abfragefilter auf den aktuellen Mandanten, `TenantId` beim Einfügen gesetzt |
| `ISoftDelete` | `Remove` markiert die Zeile als gelöscht, Abfragen blenden sie aus |
| `IHasConcurrencyToken` | optimistische Nebenläufigkeit über eine `Version`-Spalte |
| `IVersioned` | ein Schnappschuss pro Revision für die Versionshistorie |
| `[Realtime(permission)]` | Einfügen, Ändern und Löschen gehen per SignalR an Clients mit dieser Berechtigung |

Domain Events, die ein `AggregateRoot` mit `Raise(...)` auslöst, und Integration Events über `IEventBus` landen in der Outbox derselben Transaktion. `AddCoworkeeOutboxProcessing<TContext>()` verschickt sie nach dem Speichern und wiederholt Fehlschläge. Ein Event zu einer zurückgerollten Änderung geht also nie hinaus.

## PostgreSQL, SQL Server oder SQLite

`Coworkee:Database:Provider` wählt eine von drei Datenbanken. PostgreSQL ist der Standard und die Wahl für den Betrieb. SQL Server passt zu Organisationen, die ihn oder Azure SQL ohnehin betreiben. SQLite hält alles in einer Datei und ist für Demos, Prototypen und Installationen auf einem Rechner gedacht. Was je Provider anders ist (Concurrency-Token, JSON-Spalten, Zeilensperren der Outbox, verteilte Sperren, Speicher der Hintergrundjobs), steckt hinter kleinen Nahtstellen der Bibliothek; die App ändert sich nicht.

```bash
coworkee new Shop --database sqlServer
```

Migrationen gehören zu dem Provider, mit dem die App angelegt wurde. Datenbanksicherungen brauchen PostgreSQL.

## Audit pro Feld

Jede Änderung an einer auditierten Entity wird Feld für Feld geschrieben: wer, wann, alter Wert, neuer Wert. Das geschieht in derselben Transaktion wie die Änderung selbst. Die Admin-Oberfläche hat eine globale Seite *Audit-Log* und die Komponente `AuditTimeline` für die Historie einer einzelnen Entity. `[NotAudited]` lässt einen Typ oder eine Eigenschaft weg, `[Sensitive]` hält fest, dass sich eine Eigenschaft geändert hat, ohne den Wert zu speichern.

![Audit-Log mit Filtern für Entity, Aktion und Benutzer und den geänderten Feldern pro Zeile](../assets/screenshots/audit-log-dark.webp){ .shot loading=lazy }

## OData-Sets mit Facetten für jede Entity

Eine Zeile macht aus einer Entity ein OData-Set mit seiner Berechtigung:

```csharp
context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View);
```

Tabellen, Exporte und externe Werkzeuge lesen darüber: `$filter`, `$orderby`, `$select`, `$expand`, Blättern und Zählen. Die Berechtigung wird bei jedem Aufruf geprüft, Mandanten- und Soft-Delete-Filter gelten wie überall. Regeln pro Zeile gehören in einen `IODataEntityFilter<T>`, der Listen, Zählungen, Facetten und Einzelabrufe gleichermaßen einschränkt. Interne Felder lassen sich ganz aus dem Modell nehmen.

Eigenschaften mit `[ProvideFacet]` kommen auf Anfrage als Facettengruppen zurück: jede Option mit Beschriftung, Anzahl und dem OData-Filter, der sie anwendet. Die Zahlen folgen dem aktuellen Filter, so wie man es aus Online-Shops kennt. Enums, `[Flags]`-Enums, Zahlenbereiche mit Histogramm und Datumsbereiche sind abgedeckt; `[FacetBehavior]` legt fest, wie Werte verknüpft werden und welche Ansicht die Tabelle zeigt.

## Ein Abfrage-Editor für OData und LINQ

Mit `QueryBuilder` bauen Benutzer verschachtelte Gruppen aus *alle / irgendeine* Bedingungen, mit Operatoren passend zum Feldtyp. Die Abfrage ist ein einfaches Modell: `ODataQueryTranslator.ToFilter` macht daraus einen OData-`$filter`, `LinqQueryTranslator.ToExpression<T>` einen LINQ-Ausdruck, den EF Core übersetzt, und `QueryJson` speichert sie. Beide Übersetzer werden gegeneinander getestet und liefern dieselben Zeilen.

## Client-Entities: 100.000 Zeilen, beantwortet im Browser

Wird eine Entity mit `[ClientEntity]` markiert, hält der Blazor-Client eine Kopie ihres OData-Sets im Browser. Bis die Kopie vollständig ist, gehen Abfragen an den Server; danach laufen Filter, Suche, Sortierung, Gruppierung, Blättern und Facettenzählung lokal, und Änderungen kommen als kleine Deltas nach. Die Kopie ist ein kleiner Spaltenspeicher mit Indizes und liegt in IndexedDB, so dass eine neu geladene Seite nach wenigen hundert Millisekunden wieder lokal antwortet.

Das Template zeigt es mit 100.000 Aufträgen. Jede Tabelle über einem solchen Set bekommt einen Schalter mit *Vergleichen und das Schnellere wählen*: Er führt die aktuelle Abfrage der Tabelle auf jeder Seite dreimal aus, zeigt die Medianzeiten und wählt die schnellere Seite. Ohne Filter lag der gemessene Median bei 216 bis 236 ms auf dem Server und 39 bis 56 ms im Browser. Die Kopie enthält nur Zeilen, die der Benutzer auch per OData lesen darf; Berechtigungen, Mandanten und Zeilenfilter gelten also auch für sie.

## Erweiterte Attribute, Excel-Import und -Export

Mit `AddExtendedAttributes<T>` hängen Benutzer Schlüssel-Wert-Paare (Text, Dezimalzahl, Datum oder JSON) an eine Entity, ohne das Schema zu ändern. Bearbeitet werden sie mit `ExtendedAttributesEditor` oder in einem Dialog aus einer Tabellenzeile.

Jedes OData-Set lässt sich als Excel-Arbeitsmappe exportieren, mit demselben Filter und derselben Sortierung wie die Liste, begrenzt durch `Coworkee:OData:MaxExportRows` (Standard 100.000). `AddODataImport` macht aus jeder Zeile eines hochgeladenen Blatts ein Command; Validierung und Berechtigungen laufen pro Zeile, fehlgeschlagene Zeilen kommen mit ihrer Zeilennummer zurück.

## Datenbanksicherungen

Mit PostgreSQL speichert `CoworkeeBackupModule` die ganze Datenbank als eine ZIP-Datei im Dateispeicher und stellt sie in einer Transaktion wieder her. Eine Sicherung aus einem anderen Migrationsstand wird abgelehnt. Sicherungen enthalten alle Organisationen, deshalb sehen die Seite nur Benutzer der Systemorganisation mit `Backups.Manage`.

## Weiterlesen

- [Persistenz](../fundamentals/persistence.md): DbContext, Datenbank-Provider, Basisklassen, Migrationen und Seed
- [OData und Facetten](../web/odata.md): Entity-Sets, Regeln pro Zeile, Excel-Export und -Import
- [Abfrage-Editor](../ui/query-builder.md): Felder, Operatoren, Übersetzer und gespeicherte Abfragen
- [Client-Entities](../ui/client-entities.md): Einstellungen, der Schalter, Messungen und Grenzen
- [Audit](../modules/auditing.md): Audit-Log, Zeitleisten und Versionshistorie
- [Erweiterte Attribute](../modules/extended-attributes.md): Endpunkte und Client-Komponenten
- [Datenbanksicherungen](../modules/backups.md): was eine Sicherung enthält und welche Datenbankrechte eine Wiederherstellung braucht
