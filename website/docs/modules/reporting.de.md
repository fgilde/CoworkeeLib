# Berichte

`Coworkee.Reporting` (Server) und `Coworkee.Client.Blazor.Reporting` (Client) ergänzen Dashboards über die OData-Entitätsmengen deiner App. Benutzer ordnen Widgets auf einem Raster an, filtern sie gemeinsam, klicken auf einen Balken, um die übrigen einzugrenzen, und teilen das Ergebnis mit Rollen oder der ganzen Organisation. Jede Zahl aggregiert die Datenbank, ein Dashboard über 100.000 Zeilen antwortet in wenigen Millisekunden.

![Ein Vertriebs-Dashboard mit Kennzahlen, Diagrammen, Pivot und Tabelle](../assets/screenshots/reporting-dashboard.png){ .shot }

## Einrichtung

```csharp
// API: das Modul bringt Tabellen, Endpunkte, Berechtigungen und Live-Updates
[DependsOn(typeof(CoworkeeReportingModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

```csharp
// WebAssembly-Client: Seiten unter /reports und der Navigationseintrag "Reports"
builder.Services.AddCoworkeeReporting(baseAddress, reports => reports
    .DrillDown("SalesOrders", "sales/orders")      // "Daten öffnen" führt mit dem Filter des Widgets auf deine Seite
    .DrillDown("Products", "catalog/products"));
```

Nimm `typeof(Coworkee.Client.Blazor.Reporting.ReportingOptions).Assembly` in die `AdditionalAssemblies` des Routers auf (in `Routes.razor` und in `AddAdditionalAssemblies` des Web-Hosts) und erzeuge eine Migration für die Tabellen `cw.Reports` und `cw.ReportFavorites`. Neue Apps bekommen das alles mit der Template-Option `--reports` (standardmäßig an).

## Deine Entitätsmengen als Datenquellen

Berichte nutzen, was du ohnehin für OData registriert hast, sonst nichts:

```csharp
services.AddODataEntity<SalesOrder>("SalesOrders", SalesPermissions.Orders.View);
services.AddODataEntity<Customer>("Customers", SalesPermissions.Orders.View);
```

- **Felder** kommen aus dem EDM: mit `AddODataEntity(..., o => o.Secret)` verborgene Eigenschaften erscheinen nie, weder als Feld noch in einem Filter.
- **Navigationen** zu einer anderen registrierten Menge ergänzen deren Felder, etwa `Customer/Name`; der Benutzer braucht auch die Berechtigung dieser Menge.
- **Beschriftungen** kommen aus `[ProvideFacet(Label = ...)]`, `[Display(Name = ...)]` oder dem Eigenschaftsnamen; Enum-Werte zeigen ihre `[Display]`-Namen.
- **Zeilen** sind die, die auch die OData-Liste liefert: Mandantenfilter, Soft Delete und jeder `IODataEntityFilter<T>` gelten. Wer die Berechtigung der Menge nicht hat, bekommt für deren Widgets ein 403, der Rest des Berichts funktioniert weiter.
- Entitäten mit `[Realtime]` aktualisieren offene Dashboards, sobald sich ihre Zeilen ändern.

`GET /api/v1/reports/sources` listet die Mengen und Felder, die der angemeldete Benutzer nutzen darf.

## Aggregation auf dem Server

Widgets senden ein Abfragemodell, nie einen Ausdruck:

```json
POST /api/v1/reports/query
{
  "entitySet": "SalesOrders",
  "filter": "Region eq 'Nord' and Total gt 100",
  "groupBy": { "field": "OrderDate", "bucket": "Month" },
  "splitBy": { "field": "Channel" },
  "aggregates": [ { "function": "Sum", "field": "Total" }, { "function": "Count" } ],
  "sortBy": "Value", "descending": true, "top": 10
}
```

Der Server prüft jedes Feld gegen die Quelle, baut `GroupBy` und `Select` mit Expression Trees und lässt EF Core daraus SQL machen. Der Filter ist OData-`$filter`, gegen das EDM geparst wie am OData-Endpunkt. Jede Gruppe kommt mit einem OData-Filter für genau ihre Zeilen zurück, den der Client für Kreuzfilter und Drill-down nutzt.

| | |
|---|---|
| Gruppieren | nach jedem Feld, höchstens zwei (Kategorien und Reihen); Datumswerte nach `Day`, `Week` (Montag), `Month`, `Quarter`, `Year` |
| Werte | `Count`, `Sum`, `Average`, `Min`, `Max` (Zahlen), `DistinctCount` (jedes Feld), bis zu 8 |
| Grenzen | 5.000 Gruppen, Top 1 bis 1.000 |
| Abgelehnt | unbekannte oder verborgene Felder, Summen über Text, Zeiträume auf Nicht-Datumsfeldern, OData, das sich nicht parsen lässt |

Alle drei Datenbank-Provider aggregieren in SQL. Auf SQL Server werden Wochen mit `DATEDIFF` ab einem Montag gezählt, weil der Wochentag dort von Servereinstellungen abhängt. Der SQLite-Provider von EF speichert `DateTimeOffset` als Text ohne Datumsteile, deshalb lehnt SQLite Zeiträume auf `DateTimeOffset`-Feldern ab (`DateTime` und `DateOnly` funktionieren); dafür Postgres oder SQL Server nutzen.

Gemessen an den 100.000 Aufträgen der Demo (Postgres, warm): Anzahl nach Status 9 ms, Summe nach Monat 29 ms, Anzahl nach Woche 55 ms, Region nach Kanal 23 ms, Top 10 Kunden mit Join 30 ms, vier KPI-Werte 58 ms.

## Widgets

| Widget | Zeigt |
|---|---|
| Kennzahl | einen Wert mit Trend gegenüber dem Zeitraum davor, einer Kurve über die Zeit und einem Tacho zum Ziel |
| Diagramm | Balken, liegende Balken, gestapelte Balken, Linie, Fläche, Torte, Ring, Streuung, Netz, Treemap, Heatmap; Datumswerte auf einer Zeitachse |
| Tabelle | Zeilen der Menge mit endlosem Scrollen (jedes Scrollen lädt die nächste Seite), sortierbar, als Tabelle, Karten oder Liste |
| Datentabelle | die [`CoworkeeDataTable`](../ui/data-table.md) der Bibliothek: Seiten, Gruppierung, Spaltenauswahl, Facetten, Suche |
| Pivot | Kategorien als Zeilen, Reihen als Spalten, nach Größe eingefärbt, mit Summen |
| Text, Bild, Webseite | Markdown (HTML wird nicht dargestellt), ein Bild oder eine abgeschottete Seite von einer http(s)-Adresse |

Diagramme nutzen [Blazor-ApexCharts](https://github.com/apexcharts/Blazor-ApexCharts): gepflegt, animiert und ohne Nacharbeit ansehnlich, mit Klick-Ereignissen für Kreuzfilter. Farben, Schrift und Dunkelmodus kommen aus den CSS-Variablen des MudBlazor-Themes, eigene Themes brauchen keine weitere Einrichtung.

![Dasselbe Dashboard im Dunkelmodus](../assets/screenshots/reporting-dark.png){ .shot }

## Designer

![Designer mit dem Widget-Bereich](../assets/screenshots/reporting-designer.png){ .shot }

- **Bearbeiten** schaltet das Raster in den Bearbeitungsmodus: Widgets am Titel ziehen, an den Rändern in der Größe ändern, neue über *Widget hinzufügen* anlegen. Das Layout nutzt [GridStack](https://gridstackjs.com/) mit 12 Spalten und fällt auf Telefonen auf eine Spalte zurück.
- Der **Widget-Bereich** bearbeitet Datenquelle, Gruppierung, Reihen, Werte, Sortierung, Top, das Datumsfeld für den Zeitraum des Berichts, Facetten und Regeln des [Query Builders](../ui/query-builder.md), Zahlenformat, Ziel, Legende und mehr; das Widget zeichnet sich neu, während du es änderst.
- **Rückgängig** und **Wiederholen** decken jede Änderung bis zum Speichern ab. Die *Berichtseinstellungen* enthalten Name, Beschreibung, Symbol, Freigabe und das Aktualisierungsintervall.
- **Ansicht**: Zeitraum für alle Widgets, Facetten je Entitätsmenge, Kreuzfilter per Klick auf Balken, Tortenstück oder Pivot-Zelle, *Daten öffnen* an jedem Widget, Vollbild je Widget, Präsentationsmodus, Aktualisieren, Export.

## Filter

![Ein Klick auf ein Tortenstück filtert die übrigen Widgets](../assets/screenshots/reporting-cross-filter.png){ .shot }

Die Zeilen eines Widgets grenzen in dieser Reihenfolge ein: seine eigenen Query-Builder-Regeln und Facetten, die Facetten des Berichts für seine Entitätsmenge, der Zeitraum des Berichts auf dem Datumsfeld des Widgets und die Kreuzfilter anderer Widgets derselben Menge. Facetten und Zeitraum gehören zum Bericht und werden mit ihm gespeichert, Kreuzfilter gelten für den Moment.

## Freigabe und Berechtigungen

| Berechtigung | Erlaubt |
|---|---|
| `Reports.View` | mit dir geteilte Berichte öffnen, Favoriten |
| `Reports.Manage` | Berichte anlegen, eigene ändern und löschen, jeden sichtbaren duplizieren |
| `Reports.Share` | mit Rollen oder der Organisation teilen |

Ein Bericht ist *privat*, mit *Rollen* geteilt (Inhaber einer davon, direkt oder über Gruppen) oder mit der *Organisation*. Nur der Besitzer ändert ihn; andere duplizieren ihn in eine private Kopie. Eine Änderung erreicht über den [Realtime-Hub](realtime.md) jeden, der den Bericht geöffnet hat.

## Export

- Bericht: Bild (PNG), PDF über den Druckdialog des Browsers (Druckstile blenden Navigation und Werkzeuge aus) und die Berichtsdatei (`.report.json`) zum Import an anderer Stelle.
- Widget: CSV und Excel dessen, was es zeigt; Tabellen exportieren ihre geladenen Zeilen.

## Endpunkte

| Endpunkt | |
|---|---|
| `GET /api/v1/reports` | Berichte, die du öffnen kannst, Favoriten zuerst |
| `POST /api/v1/reports`, `PUT /api/v1/reports/{id}`, `DELETE /api/v1/reports/{id}` | anlegen, ändern, löschen |
| `POST /api/v1/reports/{id}/duplicate`, `PUT /api/v1/reports/{id}/favorite` | kopieren, Favorit |
| `GET /api/v1/reports/sources`, `GET /api/v1/reports/roles` | Felder je Menge, Rollen zum Teilen |
| `POST /api/v1/reports/query` | aggregieren |
| `POST /api/v1/reports/sheet` | Zeilen als Excel-Arbeitsmappe |
