# Query-Builder

`QueryBuilder` baut einen Filter per Klick: verschachtelte Gruppen *alle / eine* aus Bedingungen, jede mit einem Feld, einem Operator passend zum Typ des Felds und einem Werteeditor dieses Typs. Er weiß nichts davon, wo Daten liegen. Die Abfrage ist ein einfaches Modell, das sich in OData-`$filter`-Text und in einen LINQ-Ausdruck übersetzt und als JSON speichern lässt.

Komponente, Modell und beide Übersetzer liegen in `Coworkee.Client.Blazor.Components.Query` und nutzen nur MudBlazor, sie funktionieren also in jeder Blazor-App. Die [Datentabelle](data-table.md#erweiterter-filter) nutzt ihn als *Erweiterten Filter*.

![Query-Builder](../assets/screenshots/advanced-filter.png){ .shot }

```razor
<QueryBuilder Fields="Fields" @bind-Value="_query" Localize="text => L[text]" />

@code {
    private QueryGroup _query = new();

    private static readonly QueryField[] Fields =
    [
        new("Number", "Nummer", QueryFieldType.Text),
        new("Status", "Status", QueryFieldType.Enum) { Options = [new("Open", "Offen"), new("Shipped", "Unterwegs")] },
        new("Total", "Summe", QueryFieldType.Number),
        new("OrderDate", "Auftragsdatum", QueryFieldType.DateTime),
        new("Paid", "Bezahlt", QueryFieldType.Boolean),
        new("Customer.Name", "Kunde", QueryFieldType.Text),
        new("CustomerId", "Kunde", QueryFieldType.Guid) { Lookup = SearchCustomersAsync },
    ];

    private string? Filter => ODataQueryTranslator.ToFilter(_query, Fields);
}
```

| Parameter | |
|---|---|
| `Fields` | die angebotenen Felder |
| `Value`, `ValueChanged` | die Abfrage; der Builder ändert sie direkt und löst nach jeder Änderung `ValueChanged` aus |
| `Localize` | übersetzt die englischen Texte und Operatornamen des Builders |
| `ShowPreview` | die Vorschau als Text, OData und JSON (Standard `true`) |
| `ShowImportExport` | JSON-Import und -Download (Standard `true`) |
| `MaxDepth` | wie tief Gruppen verschachteln (3; 0 erlaubt nur Bedingungen) |
| `Dense` | kompakte Eingaben (Standard `true`) |
| `Now` | "jetzt" für relative Datumsangaben; standardmäßig die lokale Uhr |

## Felder

Ein `QueryField` hat einen Namen (den Eigenschaftspfad, `Customer.Name` oder `Customer/Name`), eine Beschriftung und einen `QueryFieldType`:

| Typ | Operatoren | Werteeditor |
|---|---|---|
| `Text` | enthält, enthält nicht, ist gleich, ist ungleich, beginnt mit, endet mit, ist eines von, ist keines von, ist leer, ist nicht leer | Text; eine Liste von Einträgen für *ist eines von* |
| `Number` | =, &ne;, >, &ge;, <, &le;, liegt zwischen, liegt nicht zwischen, ist eines von, ist keines von | Zahl |
| `Boolean` | ist ja, ist nein | |
| `Date`, `DateTime` | ist am, ist nicht am, vor, nach, am oder vor, am oder nach, zwischen, nicht zwischen, heute, diese Woche, diesen Monat, dieses Jahr, in den letzten n Tagen, in den nächsten n Tagen | Datumsauswahl, Anzahl Tage |
| `Enum` | ist eines von, ist keines von, ist, ist nicht | Auswahl der `Options` |
| `Guid` | ist, ist nicht, ist eines von, ist keines von | Auswahl der `Options`, Suche über `Lookup`, sonst Text |

`Options` machen jedes Feld zur Auswahl, `Lookup` sucht Auswahlwerte beim Tippen (`Func<string?, CancellationToken, Task<IReadOnlyList<QueryOption>>>`); eine Regel merkt sich die Beschriftungen gesuchter Werte. `Flags = true` kennzeichnet ein `[Flags]`-Enum: *ist* und *ist eines von* prüfen einzelne Flags (`Options has 'Express'`). `Nullable = true` ergänzt *ist leer* und *ist nicht leer*, `Operators` schränkt die Liste ein, `Category` gruppiert die Feldauswahl.

## Übersetzen

```csharp
string? filter = ODataQueryTranslator.ToFilter(query, fields);              // OData $filter, null wenn nichts filtert
Expression<Func<Order, bool>> predicate = LinqQueryTranslator.ToExpression<Order>(query, fields);
IQueryable<Order> rows = db.Orders.Where(query, fields);                     // EF Core übersetzt es
IEnumerable<Order> local = orders.Where(query, fields);                      // im Speicher
string text = QueryDescriber.Describe(query, fields, l);                     // "Status ist eines von Offen, Unterwegs und Summe ist mindestens 100"
```

Beide Übersetzer lesen eine Abfrage gleich:

- Text vergleicht ohne Groß-/Kleinschreibung (`contains(tolower(Name),'drill')`, `Name.ToLower().Contains("drill")`), leerer Text (null) passt nie;
- Datumswerte vergleichen ganze Tage im Offset von `now` (das optionale letzte Argument jedes Übersetzers, standardmäßig die lokale Uhr): *ist am 11. Okt.* ist `ge 2026-10-11T00:00:00+02:00 and lt 2026-10-12T00:00:00+02:00`, *diese Woche* beginnt am Montag;
- Enums vergleichen nach Membername (`Status eq 'Shipped'`), gespeicherte Abfragen überstehen also umsortierte Enums;
- eine leere Navigation liest sich als null (`Customer.Name` eines Auftrags ohne Kunde);
- unvollständige Bedingungen und leere Gruppen fallen weg, eine Abfrage in Bearbeitung ist also immer gültig;
- Hochkommas in Werten werden verdoppelt (`'O''Neil'`).

Die Tests schicken jeden Operator durch beide Übersetzer und durch die OData-Engine der [Client-Entities](client-entities.md) und erwarten dieselben Zeilen.

## Speichern

```csharp
string json = QueryJson.Serialize(query);
QueryGroup? back = QueryJson.Deserialize(json);   // null für Text, der keine Abfrage ist
```

JSON nutzt Namen für Enums und lässt leere Werte weg:

```json
{"logic":"Or","children":[{"kind":"rule","field":"Status","operator":"In","values":["Open","Shipped"]},
 {"kind":"group","logic":"And","children":[{"kind":"rule","field":"Total","operator":"GreaterThanOrEqual","value":"100"}]}]}
```

Werte sind sprachneutraler Text: Zahlen mit Punkt, Datumswerte als `yyyy-MM-dd`, Enums nach Membername, eine Abfrage liest sich also in jeder Sprache gleich. Die Schaltflächen der Vorschau laden die Abfrage herunter und laden eine aus eingefügtem JSON.

## Modell

| Typ | |
|---|---|
| `QueryGroup` | `Logic` (`And`, `Or`), `Not`, `Children`; `Rules()` durchläuft alle Regeln, `IsEmpty` |
| `QueryRule` | `Field`, `Operator`, `Value`, `Value2` (Obergrenze von *zwischen*), `Values` (*ist eines von*), `Labels` |
| `QueryOperators` | `For(field)`, `NeedsValue`, `IsList`, `IsRange`, `IsComplete`, `Label(op, type)` |
| `QueryDates` | der Tagesbereich einer Datumsregel |

Die Teile lassen sich auch einzeln rendern: `QueryGroupEditor`, `QueryRuleEditor` und `QueryValueEditor` für ein eigenes Layout.
