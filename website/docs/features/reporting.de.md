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

![Ein Vertriebs-Dashboard mit Kennzahlen, Diagrammen, Pivot und Tabelle](../assets/screenshots/reporting-dashboard.png){ .shot loading=lazy }

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

![Berichtsdesigner mit geöffnetem Widget-Bereich](../assets/screenshots/reporting-designer.png){ .shot loading=lazy }

Das Raster ist GridStack mit 12 Spalten, auf dem Handy eine Spalte. Im Bearbeitungsmodus ziehst du Widgets am Titel, änderst ihre Größe an den Rändern und stellst jedes im Seitenbereich ein; es zeichnet sich neu, während du tippst. Rückgängig geht bis zum Speichern.

Widget-Typen: Kennzahl mit Trend und Ziel, Diagramme (Balken, Linie, Fläche, Torte, Ring, Streuung, Netz, Treemap, Heatmap und ein paar Varianten), eine scrollende Tabelle, die vollständige `CoworkeeDataTable`, ein Pivot, dazu Markdown, Bild oder eingebettete Seite für den Text rund um die Zahlen. Die Farben folgen dem MudBlazor-Theme, Dunkelmodus inklusive.

Jede Gruppe kommt vom Server mit einem OData-Filter für genau ihre Zeilen zurück. Ein Klick auf einen Balken oder ein Tortenstück grenzt damit die anderen Widgets derselben Menge ein; *Daten öffnen* springt auf die Seite, die du mit `DrillDown` registriert hast, schon gefiltert auf die Zeilen hinter der Zahl.

Ein Bericht ist privat, für Rollen oder für die ganze Organisation freigegeben (`Reports.Share`). Andere können ihn duplizieren, ändern darf ihn nur der Besitzer, und eine gespeicherte Änderung erreicht alle, die ihn gerade offen haben. Exportieren lässt er sich als PNG, als PDF über den Druckdialog oder je Widget als CSV und Excel.

Referenz: [Berichte](../modules/reporting.md), [OData](../web/odata.md), [Query Builder](../ui/query-builder.md).
