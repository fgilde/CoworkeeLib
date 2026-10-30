# Berichte

`Coworkee.Reporting` (Server) und `Coworkee.Client.Blazor.Reporting` (Client) ergänzen Dashboards über die OData-Entitätsmengen deiner App. Benutzer ordnen Widgets auf einem Raster an, filtern sie gemeinsam, klicken auf einen Balken, um die übrigen einzugrenzen, und teilen das Ergebnis mit Rollen, Gruppen oder der ganzen Organisation. Jede Zahl aggregiert die Datenbank, ein Dashboard über 100.000 Zeilen antwortet in wenigen Millisekunden.

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
- **Navigationen** zu einer anderen registrierten Menge ergänzen deren Felder, etwa `Customer/Name`; der Benutzer braucht auch die Berechtigung dieser Menge, und ihre Zeilenfilter gelten: ein Kunde, den der Benutzer nicht sehen darf, zählt als leer (`null`), nie unter seinem Namen.
- **Beschriftungen** kommen aus `[ProvideFacet(Label = ...)]`, `[Display(Name = ...)]` oder dem Eigenschaftsnamen; Enum-Werte zeigen ihre `[Display]`-Namen.
- **Zeilen** sind die, die auch die OData-Liste liefert: Mandantenfilter, Soft Delete und jeder `IODataEntityFilter<T>` gelten. Wer die Berechtigung der Menge nicht hat, bekommt für deren Widgets ein 403, der Rest des Berichts funktioniert weiter.
- Entitäten mit `[Realtime]` aktualisieren offene Dashboards, sobald sich ihre Zeilen ändern.

`GET /api/v1/reports/sources` listet die Mengen und Felder, die der angemeldete Benutzer nutzen darf.

## Eigene Datenquellen

Manche Kennzahlen sind keine Zeilen einer Tabelle: Mitarbeiterzahl pro Monat aus Eintritts- und Austrittsdatum, Fluktuation oder Krankenquote pro Monat, Überstunden pro Abteilung, Schulungsquote. Registriere sie als Quelle mit einem einfachen Zeilentyp; Berichte listen sie nach den Entitätsmengen (mit einem Summen-Symbol markiert) und aggregieren ihre Zeilen wie die einer Menge:

```csharp
public sealed record HeadcountRow(
    DateOnly Month,
    string Department,
    [property: DisplayName("Mitarbeitende")] int Headcount,
    int Hires,
    int Exits);

services.AddReportSource<HeadcountRow>("HeadcountByMonth", "Mitarbeitende pro Monat", HrPermissions.Reports,
    async (context, cancellationToken) =>
    {
        // die Mitarbeitenden, die der Benutzer lesen darf: Mandant, Soft Delete und die Zeilenfilter der Menge gelten
        var employees = await ODataEntityAccess.VisibleAsync<Employee>(context.Services, cancellationToken);
        var from = context.DateFrom ?? new DateOnly(DateTime.Today.Year - 1, 1, 1);
        var to = context.DateTo ?? DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        var people = await employees
            .Where(e => e.HireDate < to && (e.ExitDate == null || e.ExitDate >= from))
            .Select(e => new { e.HireDate, e.ExitDate, Department = e.Department!.Name })
            .ToListAsync(cancellationToken);

        var rows = new List<HeadcountRow>();
        for (var month = new DateOnly(from.Year, from.Month, 1); month < to; month = month.AddMonths(1))
        {
            var end = month.AddMonths(1);
            rows.AddRange(people.GroupBy(p => p.Department).Select(g => new HeadcountRow(month, g.Key,
                g.Count(p => p.HireDate < end && (p.ExitDate is not { } exit || exit >= end)),
                g.Count(p => p.HireDate >= month && p.HireDate < end),
                g.Count(p => p.ExitDate >= month && p.ExitDate < end))));
        }

        return rows;
    });
```

- **Felder** sind die einfachen öffentlichen Eigenschaften des Zeilentyps (Text, Zahlen, Datumswerte, Wahrheitswerte, Enums, GUIDs), beschriftet wie bei Entitätsmengen (`[Display]`, `[DisplayName]`, `[ProvideFacet(Label = ...)]`, `[Display]`-Namen der Enum-Werte). Der Name darf nicht der einer OData-Menge sein.
- **Berechtigung** wirkt wie die einer Menge: Ohne sie fehlt die Quelle in der Liste und ihre Abfragen antworten mit 403. Die Zeilen sind das, was deine Funktion liefert; Zeilenregeln wendet sie also selbst an, z. B. über `ODataEntityAccess.VisibleAsync<T>` oder den aktuellen Benutzer aus `context.Services`.
- **Kontext**: `context.Services` sind die Dienste der Anfrage. `DateFrom`/`DateTo` sind die Grenzen, die ein Datumsfeld im Filter des Widgets bekommt (der Zeitraum des Berichts schickt `Month ge 2026-01-01 and Month lt 2026-04-01`), damit die Quelle nur diese Monate berechnet. Sie sind ein Hinweis: Der Filter gilt trotzdem für die gelieferten Zeilen.
- **Aggregation**: Filter des Widgets (OData `$filter`, gegen den Zeilentyp geparst), Gruppierung mit Datums-Buckets, Werte und Top funktionieren wie bei Mengen. Eine Liste wird im Speicher aggregiert; gib ein `IQueryable` der Datenbank zurück (das `Task<IEnumerable<T>>` nimmt es an), dann aggregiert die Datenbank. Es gibt auch eine synchrone Überladung `AddReportSource<T>(name, label, permission, context => rows)`.
- Werte, die ein Bestand pro Monat sind (Mitarbeiterzahl), addieren sich pro Quartal; nimm dafür `Average` oder `Max` und `Sum` für Bewegungen wie Eintritte.
- **Widgets**: KPI, Diagramme, Pivot und Tabelle funktionieren; Tabellen lesen die Zeilen über `POST /api/v1/reports/rows`. Das Datentabellen-Widget, Facetten in der Filterleiste und die Echtzeit-Aktualisierung brauchen eine OData-Menge (nimm stattdessen das Aktualisierungsintervall des Berichts). *Daten öffnen* erscheint nur, wenn der Client eine Seite für die Quelle registriert: `ReportingOptions.DrillDown("HeadcountByMonth", "/hr/people")`.
- **KI**: Die Berichtswerkzeuge des Assistenten und des MCP-Servers (`get_report_sources`, `run_report`, `read_report_rows`, `generate_report`) sehen die Quellen, die der Benutzer lesen darf; `list_data_sets`/`query_data` decken nur OData-Mengen ab.

### Einen Bericht auf einer Quelle anlegen (Seed)

Ein Bericht speichert nur den Namen der Quelle; eine App legt ihn also wie jeden Bericht an, z. B. in ihrem Migrations-Host:

```csharp
var definition = new ReportDefinition
{
    DateRange = new ReportDateRange(DateRangePreset.Last12Months),
    Widgets =
    [
        new ReportWidget
        {
            Id = "trend", Type = WidgetType.Chart, Title = "Mitarbeitende pro Abteilung", W = 12, H = 5,
            Query = new WidgetQuery
            {
                EntitySet = "HeadcountByMonth", DateField = "Month",
                GroupBy = new ReportGroupBy("Month", DateBucket.Month), SplitBy = new ReportGroupBy("Department"),
                Aggregates = [new(AggregateFunction.Sum, "Headcount")],
            },
            Options = new WidgetOptions { Chart = ChartKind.Line },
        },
    ],
};
db.Add(new Report
{
    TenantId = tenantId, OwnerId = adminId, Name = "Mitarbeitende", Visibility = ReportVisibility.Organisation,
    Definition = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
});
```

Name und Beschreibung des Berichts, Widget-Titel und Wertbeschriftungen werden gespeichert, wie sie geschrieben sind, und übersetzt angezeigt, wenn die App einen Text dafür hat (ihre `Localization/*.json`); ein Seed schreibt also englische Schlüssel und liefert deren Übersetzungen mit. Eigene Namen ohne Text erscheinen, wie sie eingegeben wurden.

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

Alle drei Datenbank-Provider aggregieren in SQL. Auf SQL Server werden Wochen mit `DATEDIFF` ab einem Montag gezählt, weil der Wochentag dort von Servereinstellungen abhängt. Auf SQLite speichert ein Coworkee-DbContext `DateTimeOffset` als Zahl; Zeiträume berechnen daraus das UTC-Datum. Ein anders gespeicherter `DateTimeOffset` (ein [externer DbContext](../fundamentals/persistence.md#externe-datenbanken), auch mit dem Binär-Converter, oder ein eigenes `HasConversion`) hat auf SQLite keine Datumsteile, seine Zeiträume werden dort abgelehnt.

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

## Umschalter für Betrachter

Ein Widget kann Betrachtern andere Werte und Gruppierungen anbieten, ohne ein zweites Widget: *Umschalter für Betrachter* im Widget-Bereich fügt Alternativen hinzu, jede beginnt als Kopie des eigenen Werts oder der eigenen Gruppierung des Widgets.

- **Werte** (`WidgetOptions.Metrics`, eine Liste von `MetricView`): Bezeichnung, Aggregate, Zahlenformat und ein zusätzlicher OData-Filter, der mit dem eigenen Filter des Widgets und-verknüpft wird. Kennzahlen, Diagramme und Pivots wechseln ihre Werte; Tabellen übernehmen nur den Filter (z. B. *Offene Aufträge* neben *Alle*).
- **Gruppierungen** (`WidgetOptions.Buckets`, eine Liste von `BucketView`): Bezeichnung, Gruppierungsfeld und Datumsstufe, optional ein anderes Datumsfeld für den Zeitraum des Berichts. Diagramme und Pivots gruppieren neu; Kennzahlen ändern die Stufe ihrer Kurve.

Betrachter wählen sie in Auswahllisten im Kopf des Widgets; der eigene Eintrag des Widgets steht zuerst. Die Wahl lädt das Widget mit der Alternative neu (der Bericht bleibt unverändert), wirkt mit Facetten, Zeitraum und Kreuzfiltern zusammen und ist das, was *CSV/Excel exportieren* schreibt. Sie bleibt je Benutzer, Bericht und Widget in der Benutzereinstellung `Reports.Views` erhalten. Beim Speichern wird jede Alternative wie das Widget selbst geprüft; eine mit unbekanntem Feld oder kaputtem Filter wird abgelehnt (`400`, Schlüssel `Widgets[{id}].Metrics[{i}]...`).

```csharp
new ReportWidget
{
    Type = WidgetType.Chart,
    Title = "Aufträge",
    Query = new WidgetQuery { EntitySet = "SalesOrders", GroupBy = new ReportGroupBy("Region"), DateField = "OrderDate" },
    Options = new WidgetOptions
    {
        Metrics = [new MetricView("Umsatz", [new(AggregateFunction.Sum, "Total")], NumberFormat.Currency)],
        Buckets = [new BucketView("Nach Monat", "OrderDate", DateBucket.Month)],
    },
};
```

## Schwellenwerte

Ein Widget kann `AppData` tragen, je Schlüssel ein JSON-Objekt, das eine App oder ein zweiter Client mit ihm aufbewahrt (z. B. eigene Einstellungen des Widgets); Berichte lesen und ändern es nicht, es wird mit dem Bericht kopiert und ist auf 20.000 Zeichen je Widget begrenzt.

Ein Widget kann `AppData` tragen, je Schlüssel ein JSON-Objekt, das eine App oder ein zweiter Client mit ihm aufbewahrt (z. B. eigene Einstellungen des Widgets); Berichte lesen und ändern es nicht, es wird mit dem Bericht kopiert und ist auf 20.000 Zeichen je Widget begrenzt.

*Schwellenwerte* im Widget-Bereich färben einen Wert nach Bändern (`WidgetOptions.Thresholds`, eine Liste von `ThresholdStop`): jeder Schwellenwert hat einen Startwert, eine Farbe und eine Bezeichnung, und ein Wert nimmt das Band des höchsten Schwellenwerts, den er erreicht (Werte unter dem ersten behalten ihre eigene Farbe). Farben sind `success`, `warning`, `error` und `info` des Themes (sie folgen eigenen Themes und dem Dunkelmodus) oder ein eigenes `#rrggbb`; alles andere wird beim Speichern abgelehnt.

| Widget | Was die Bänder färben |
|---|---|
| Kennzahl | den Wert und den Tacho, mit Symbol und Bezeichnung des Bands unter dem Wert |
| Diagramm | mit *Balken nach Schwellenwert färben* (`ColorBarsByThreshold`) jeden Balken eines Balken- oder liegenden Balkendiagramms; eine Legende nennt die Bänder in Worten |
| Pivot | die Zellen, mit dem Symbol des Bands |
| Tabelle | die Zellen der Zahlenspalte `ThresholdColumn`, mit dem Symbol des Bands |

Farbe ist nie das einzige Zeichen: jedes Band zeigt ein Symbol (Haken, Warnung, Fehler, Info) und seine Bezeichnung als Text oder Tooltip. Auch die Mails von Abos zeigen die Bänder der Kennzahlen, mit ✓, !, ✕ oder i vor dem Wert. *Bänder aus dem Ziel* legt drei Bänder aus dem `Target` des Widgets und *Höher ist besser* an: unter 80 % / nahe / im Ziel, bei niedriger ist besser im Ziel / bis 120 % / darüber.

## Einen Bericht mit KI erstellen

Mit eingerichtetem KI-Modell (*Administration › KI-Einstellungen*, siehe [KI](ai.md)) bekommen Inhaber von `Reports.Manage` **Mit KI erstellen** in der Berichtsliste und **KI bitten, diesen Bericht zu ändern** (Funken-Symbol) im Designer. Das Eingabefeld schlägt drei Beispiele vor, die aus den eigenen Datenmengen des Benutzers gebaut sind.

Der Server (`GenerateReportDefinitionQuery`, `POST /api/v1/reports/generate`) schreibt die Anweisungen aus dem, was der Benutzer lesen darf: jede sichtbare Menge mit Bezeichnung, Feldpfaden, Arten und Enum-Werten (berechnete Quellen gekennzeichnet), die Widget-Typen, Diagrammarten, Aggregate, Datumsstufen, Optionen samt Umschaltern und Schwellenwerten, die Regeln des 12-Spalten-Rasters, eine kleine Beispieldefinition und die Sprache des Benutzers für Titel. Er nimmt das JSON des Modells, prüft es wie beim Speichern (Validator und jedes Widget samt Alternativen gegen die Quellen) und fragt bei Fehlern das Modell mit der Fehlerliste erneut, höchstens zweimal. Die Definition kommt **ungespeichert** zurück:

- ein neuer Bericht öffnet sich unter `reports/new` im Designer; *Speichern* legt ihn an, *Abbrechen* verwirft ihn;
- eine Änderung ersetzt die Definition des Designers als ein Schritt des Verlaufs, *Rückgängig* holt die alte zurück.

Ohne Modell antwortet der Endpunkt mit `403` und die Schaltflächen bleiben verborgen (`GET /api/v1/reports/ai` liefert `{ "available": false }`); liefert das Modell dreimal keinen brauchbaren Bericht, endet es in einem `400` mit den letzten Fehlern.

Der Assistent und MCP-Clients bekommen dasselbe als lesendes Werkzeug `generate_report` (Prompt, optional die aktuelle Definition). `create_report` und `update_report` speichern das Ergebnis; als Befehle laufen sie im Assistenten erst, nachdem der Benutzer den Aufruf bestätigt hat.

## Filter

![Ein Klick auf ein Tortenstück filtert die übrigen Widgets](../assets/screenshots/reporting-cross-filter.png){ .shot }

Die Zeilen eines Widgets grenzen in dieser Reihenfolge ein: seine eigenen Query-Builder-Regeln und Facetten, die Facetten des Berichts für seine Entitätsmenge, der Zeitraum des Berichts auf dem Datumsfeld des Widgets und die Kreuzfilter anderer Widgets derselben Menge. Facetten und Zeitraum gehören zum Bericht und werden mit ihm gespeichert, Kreuzfilter gelten für den Moment.

## Freigabe und Berechtigungen

| Berechtigung | Erlaubt |
|---|---|
| `Reports.View` | mit dir geteilte Berichte öffnen, Favoriten |
| `Reports.Manage` | Berichte anlegen, eigene ändern und löschen, jeden sichtbaren duplizieren |
| `Reports.Share` | mit Rollen, Gruppen oder der Organisation teilen |
| `Reports.Dashboard` | das Dashboard der Organisation wählen |
| `Reports.Subscriptions` | die Berichtsabos der Organisation sehen und beenden |

Ein Bericht ist *privat*, mit *Rollen* geteilt (Inhaber einer davon, direkt oder über Gruppen), mit *Gruppen* oder mit der *Organisation*. Mit Gruppen geteilt erreicht er die Mitglieder dieser Gruppen und der Gruppen darüber: die aktiven Gruppen des Benutzers (`IGroupScope.ActiveAsync`), eine Gruppenauswahl grenzt die Berichtsliste also ein wie die Daten. Der Besitzer teilt nur mit seinen Gruppen und den Gruppen darunter (`GET /api/v1/reports/groups`); früher geteilte Gruppen bleiben, wenn er eine verlässt. Der Besitzer sieht den Bericht immer. Nur der Besitzer ändert ihn; andere duplizieren ihn in eine private Kopie. Eine Änderung erreicht über den [Realtime-Hub](realtime.md) jeden, der den Bericht geöffnet hat.

## Abos per Mail

*Per Mail abonnieren* (Briefumschlag-Symbol) an einem Bericht schickt ihn nach Plan: jeden Tag, jede Woche an einem Wochentag oder jeden Monat an einem Tag (kürzere Monate nehmen ihren letzten Tag), zu einer Uhrzeit einer Zeitzone (IANA-Ids wie `Europe/Berlin`; die Zeit folgt der Sommerzeit, eine übersprungene Uhrzeit läuft direkt nach der Lücke, eine doppelte beim ersten Mal). Der Dialog kann den Zeitraum des Berichts ersetzen (z. B. *Letzter Monat* für eine monatliche Mail) und die gerade gewählten Facetten übernehmen; der Excel-Anhang lässt sich abschalten.

Die Mail (Vorlage `Reports.Subscription`, in jeder mitgelieferten Sprache, bearbeitbar unter *Mail-Vorlagen*) zeigt jede Kennzahl mit Wert, Schwellenwert-Farbe, Zeichen und Bezeichnung und die ersten zehn Zeilen jedes Diagramms, Pivots und jeder Tabelle (ein Diagramm über einen Datumsbereich zeigt seine neuesten zehn, die älteste zuerst), mit einem Link zum Bericht. Sie ist in der Sprache des Abonnenten geschrieben, sonst in der Standardsprache der Organisation (Seite Sprachen), wie die Abmeldeseite. Die angehängte Arbeitsmappe hat ein Blatt je Widget mit allen Zeilen (Tabellen bis 1.000), geschrieben wie *Excel exportieren*. Bilder der Widgets oder ein PDF werden auf dem Server nicht erzeugt.

Der wiederkehrende Job `coworkee-report-subscriptions` (alle fünf Minuten, `Coworkee:Reporting:SubscriptionCron`) führt die fälligen Abos Organisation für Organisation aus, jedes **als der Abonnent**: der Bericht muss ihm noch freigegeben sein, und seine Berechtigungen, geschützten Felder und Zeilenfilter bestimmen, was die Mail zeigt. Steht etwas im Weg (Bericht nicht mehr freigegeben, keine aktive Mail-Adresse, `Reports.View` entzogen), wird nichts gesendet und das Abo zeigt den Grund; der nächste Lauf versucht es erneut. Für ihre Links brauchen die Mails `Coworkee:Notifications:PublicAppUrl`.

Jede Mail endet mit einem Link, der das Abo ohne Anmeldung beendet (ein geschütztes Token; das Öffnen fragt zuerst, die Bestätigung schickt ein Formular). *Berichte › Abos* (`reports/subscriptions`) listet die Abos des Benutzers mit Plan, nächster und letzter Mail und dem letzten Fehler, zum Ändern, sofort Senden oder Beenden. Inhaber von `Reports.Subscriptions` wechseln zu *Alle in der Organisation* und sehen und beenden alle. Gesendete Mails samt Anhängen gehören zum Mail-Log und folgen dessen Aufbewahrung.

| Endpunkt | |
|---|---|
| `GET /api/v1/reports/subscriptions`, `GET /api/v1/reports/subscriptions/all` | die Abos des Benutzers, die der Organisation (`Reports.Subscriptions`) |
| `POST /api/v1/reports/subscriptions`, `PUT …/{id}`, `DELETE …/{id}` | abonnieren, ändern, beenden (fremde mit `Reports.Subscriptions`) |
| `POST /api/v1/reports/subscriptions/{id}/send` | jetzt senden, der Plan bleibt |
| `GET`/`POST /api/v1/reports/subscriptions/unsubscribe` | die Abmeldeseite des Mail-Links (anonym, ohne `X-CSRF`) |

## Ein Bericht als Dashboard

Jeder Bericht, den du öffnen kannst, kann dein Dashboard sein: *Als mein Dashboard festlegen* im Menü seiner Karte oder auf der Berichtsseite. Inhaber von `Reports.Dashboard` (Administratoren) legen außerdem das Dashboard der Organisation fest, das jeder sieht, der selbst keins gewählt hat. Deine eigene Wahl gewinnt; einen Bericht, den du (nicht mehr) öffnen kannst, überspringt sie, und wer einen Bericht löscht, nimmt ihn auch als Dashboard heraus.

`ReportDashboard` zeigt es an. Leg es um das eigene Dashboard der App, dann sehen Benutzer ohne gewählten Bericht oder ohne `Reports.View` weiter dieses; ohne Inhalt zeigt es einen Hinweis, einen Bericht zu wählen. Eine geänderte Wahl erreicht offene Tabs sofort (Realtime-Topic `type:DashboardReport`).

```razor
@using Coworkee.Client.Blazor.Reporting.Components

<ReportDashboard>
    <PageHeader Title="Dashboard" />
    @* das eigene Dashboard der App *@
</ReportDashboard>
```

Die Dashboard-Seite des Templates (*Persönlich > Dashboard*, über *Berichte*) macht genau das, und mit Beispielen macht die Einrichtung den Bericht *Catalog overview* zum Dashboard der Organisation. Der Seed legt dazu neben dem Bericht eine Zeile `DashboardReport` an (`UserId` null für die Organisation).

## Export

- Bericht: Bild (PNG), PDF über den Druckdialog des Browsers (Druckstile blenden Navigation und Werkzeuge aus) und die Berichtsdatei (`.report.json`) zum Import an anderer Stelle.
- Widget: CSV und Excel dessen, was es zeigt; Tabellen exportieren ihre geladenen Zeilen.

## Endpunkte

| Endpunkt | |
|---|---|
| `GET /api/v1/reports` | Berichte, die du öffnen kannst, Favoriten zuerst |
| `POST /api/v1/reports`, `PUT /api/v1/reports/{id}`, `DELETE /api/v1/reports/{id}` | anlegen, ändern, löschen |
| `POST /api/v1/reports/{id}/duplicate`, `PUT /api/v1/reports/{id}/favorite` | kopieren, Favorit |
| `GET /api/v1/reports/sources`, `GET /api/v1/reports/roles`, `GET /api/v1/reports/groups` | Felder je Menge, Rollen und Gruppen zum Teilen |
| `POST /api/v1/reports/query` | aggregieren |
| `POST /api/v1/reports/rows` | Zeilen einer eigenen Quelle, für Tabellen |
| `POST /api/v1/reports/sheet` | Zeilen als Excel-Arbeitsmappe |
| `GET /api/v1/reports/ai`, `POST /api/v1/reports/generate` | ob KI verfügbar ist, eine Definition aus einem Prompt (nicht gespeichert) |
| `GET /api/v1/reports/dashboard`, `PUT /api/v1/reports/dashboard`, `PUT /api/v1/reports/dashboard/organisation` | der Dashboard-Bericht, den du siehst, deinen oder den der Organisation wählen (`{ "reportId": null }` setzt zurück) |
