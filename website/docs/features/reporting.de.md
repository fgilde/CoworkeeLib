---
title: "Dashboards und Berichte für Blazor-Apps"
description: "Dashboards zum Selbstbauen für Blazor und ASP.NET Core: Designer mit Drag and Drop, Kennzahlen, Diagramme und Pivots über deine OData-Mengen, Kreuzfilter, SQL-Aggregation."
---

# Dashboards und Berichte

```csharp
[DependsOn(typeof(CoworkeeReportingModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;

builder.Services.AddCoworkeeReporting(baseAddress, reports => reports
    .DrillDown("SalesOrders", "sales/orders"));
```

Mehr Einrichtung ist es nicht (dazu eine Migration für `cw.Reports` und `cw.ReportFavorites`). Neue Apps aus dem Template haben das schon, `--reports` ist standardmäßig an. Die Benutzer bekommen Seiten unter `/reports` und bauen dort Dashboards aus den Entitätsmengen, die du für OData registriert hast:

```csharp
services.AddODataEntity<SalesOrder>("SalesOrders", SalesPermissions.Orders.View);
```

Kein zweites Datenmodell, kein Export-Job. Die Felder kommen aus dem EDM, eine Eigenschaft, die du vor OData versteckst, taucht also auch im Bericht nicht auf. Die Zeilen sind die, die auch die OData-Liste liefern würde, mit Mandantenfilter und allen Zeilenfiltern. Fehlt jemandem das Recht für eine Menge, zeigen deren Widgets einen Fehler, der Rest des Dashboards läuft weiter.

![Die Vertriebsübersicht der Demo-App: vier Kennzahlen mit Trend, Umsatz pro Monat und Aufträge nach Status](../assets/screenshots/reporting-overview-light.webp){ .shot loading=lazy }

## Aggregiert in SQL

Ein Widget schickt ein kleines Abfragemodell an `POST /api/v1/reports/query`, nie einen Ausdruck:

```json
{
  "entitySet": "SalesOrders",
  "filter": "Region eq 'Nord' and Total gt 100",
  "groupBy": { "field": "OrderDate", "bucket": "Month" },
  "aggregates": [ { "function": "Sum", "field": "Total" }, { "function": "Count" } ],
  "top": 10
}
```

Der Server prüft jedes Feld gegen die Quelle, baut `GroupBy` und `Select` als Expression Trees und überlässt das SQL dem EF Core, auf PostgreSQL, SQL Server und SQLite gleichermaßen. Bis zu zwei Gruppierungsfelder (Datumswerte nach Tag, Woche, Monat, Quartal oder Jahr), bis zu 8 Werte aus `Count`, `Sum`, `Average`, `Min`, `Max` und `DistinctCount`, höchstens 5.000 Gruppen.

Gemessen an den 100.000 Aufträgen der Demo (Postgres, warm): Anzahl nach Status 9 ms, Summe nach Monat 29 ms, Anzahl nach Woche 55 ms, Region nach Kanal 23 ms, Top 10 Kunden mit Join 30 ms, vier KPI-Werte 58 ms.

**Stand:** Berichte haben noch keine Versionsgeschichte, keinen öffentlichen Link und kein Karten-Widget. Unter SQLite werden Zeiträume auf `DateTimeOffset`-Feldern abgelehnt, weil EF sie dort als Text speichert; `DateTime` und `DateOnly` funktionieren.

## Designer, Kreuzfilter und Drill-down

![Berichtsdesigner im Bearbeitungsmodus mit den Einstellungen eines Balkendiagramms](../assets/screenshots/reporting-designer-dark.webp){ .shot loading=lazy }

Das Raster ist GridStack mit 12 Spalten, auf dem Handy eine Spalte. Im Bearbeitungsmodus ziehst du Widgets am Titel, änderst ihre Größe an den Rändern und stellst jedes im Seitenbereich ein; es zeichnet sich neu, während du tippst. Rückgängig geht bis zum Speichern.

Widget-Typen: Kennzahl mit Trend und Ziel, Diagramme (Balken, Linie, Fläche, Torte, Ring, Streuung, Netz, Treemap, Heatmap und ein paar Varianten), eine scrollende Tabelle, die vollständige `CoworkeeDataTable`, ein Pivot, dazu Markdown, Bild oder eingebettete Seite für den Text rund um die Zahlen. Die Farben folgen dem MudBlazor-Theme, Dunkelmodus inklusive.

Jede Gruppe kommt vom Server mit einem OData-Filter für genau ihre Zeilen zurück. Ein Klick auf einen Balken oder ein Tortenstück grenzt damit die anderen Widgets derselben Menge ein; *Daten öffnen* springt auf die Seite, die du mit `DrillDown` registriert hast, schon gefiltert auf die Zeilen hinter der Zahl.

Ein Bericht ist privat, für Rollen oder für die ganze Organisation freigegeben (`Reports.Share`). Andere können ihn duplizieren, ändern darf ihn nur der Besitzer, und eine gespeicherte Änderung erreicht alle, die ihn gerade offen haben. Exportieren lässt er sich als PNG, als PDF über den Druckdialog oder je Widget als CSV und Excel.

## Der Vertriebsbericht der Demo, Schritt für Schritt

Die Demo-App bringt drei Berichte mit. Die Bilder unten stammen alle aus *Sales overview*, der die 100.000 Aufträge liest. Die Screenshots sind auf Englisch.

![Die Berichtsliste mit drei Berichten und den Filtern All, Favorites, Mine und Shared with me](../assets/screenshots/reporting-list-dark.webp){ .shot loading=lazy }

`/reports` listet jeden Bericht, den der Benutzer öffnen darf. Jede Karte zeigt, wer ihn gebaut hat, mit wem er geteilt ist, wie viele Widgets er hat und welche Entitätsmengen er liest. *Import* nimmt einen Bericht, der in einer anderen Installation als JSON exportiert wurde.

![Bearbeitungsmodus: das Balkendiagramm ist ausgewählt, rechts sind seine Einstellungen offen](../assets/screenshots/reporting-designer-light.webp){ .shot loading=lazy }

Im Bearbeitungsmodus bekommt jedes Widget einen Griff zum Verschieben und einen Einstellungsknopf. Der Bereich rechts ist die ganze Definition von *Revenue by region and channel*: Quelle *Sales orders*, gruppiert nach Region, eine Reihe pro Kanal, die Summe von `Total` und das Datumsfeld, auf das der Zeitraum *Last 12 months* des Berichts wirkt. Wählst du ein anderes Gruppierungsfeld, zeichnet sich das Diagramm sofort neu; gespeichert wird erst mit *Save*.

![Nach einem Klick auf den Balken Nord zeigt das Pivot nur noch Nord, die Top-Kunden wechseln](../assets/screenshots/reporting-cross-filter-dark.webp){ .shot loading=lazy }

Ein Klick auf den Balken *Nord*. Das Pivot darunter hat nur noch eine Zeile, die Top 10 sind jetzt Kunden aus Nord, und die Aufträge pro Woche fallen auf etwa 130. Ein zweiter Klick auf den Balken nimmt den Filter wieder weg.

![Die Auftragsseite, aus dem Widget geöffnet, mit einem Report-Chip über der Tabelle](../assets/screenshots/reporting-drill-down-dark.webp){ .shot loading=lazy }

*Open the data* im Widget-Menü springt auf die Auftragsseite. Der Chip *Report: Revenue by region and channel* trägt den Filter des Widgets, Tabelle und Facetten links zeigen also die 33.050 Aufträge der letzten zwölf Monate statt aller 100.000. Nimmst du den Chip weg, siehst du wieder alles.

![Das Widget Latest orders, von Tabelle auf Karten umgeschaltet](../assets/screenshots/reporting-table-light.webp){ .shot loading=lazy }

*Latest orders* ist dieselbe Datentabelle wie auf den Listenseiten. Die Knöpfe im Kopf schalten die Ansicht um, hier von Zeilen auf Karten; Sortierung und Spalten funktionieren wie in jeder anderen Tabelle.

![Pivot der Aufträge nach Region und Status mit Summen](../assets/screenshots/reporting-pivot-light.webp){ .shot loading=lazy }

Das Pivot stellt Regionen in die Zeilen und Status in die Spalten, mit Summen am Ende jeder Zeile und Spalte. Die größten Werte bekommen eine dunklere Zelle, deshalb sticht *Delivered* heraus.

![Der Bericht auf dem Handy: ein Widget pro Zeile](../assets/screenshots/reporting-phone-light.webp){ .shot loading=lazy width=300 }

Auf dem Handy hat das Raster eine Spalte, und die Widgets kommen in der Reihenfolge des Desktop-Layouts.

Referenz: [Berichte](../modules/reporting.md), [OData](../web/odata.md), [Query Builder](../ui/query-builder.md).
