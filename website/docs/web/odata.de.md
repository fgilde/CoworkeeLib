# OData und Facetten

Jede Entity wird mit einer Zeile zu einem OData-Set. Tabellen, Exporte und externe Werkzeuge lesen darüber; Listenabfragen schreibst du nicht.

```csharp
[DependsOn(typeof(CoworkeeODataModule))]
public sealed class MyAppCatalogModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddODataEntity<Brand>("Brands", CatalogPermissions.Brands.View);
        context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View);
    }
}
```

| Anfrage | Ergebnis |
|---|---|
| `GET /odata/Products?$filter=Rate gt 20&$orderby=Name&$top=25&$skip=50&$count=true` | eine Seite, Gesamtzahl |
| `GET /odata/Products?$expand=Brand` | Produkte mit ihrer Marke |
| `GET /odata/Products({id})` | ein Produkt |
| `GET /odata/Products?$select=Name,Rate` | nur diese Spalten |

Die Berechtigung des Sets wird bei jedem Aufruf geprüft, Mandanten- und Soft-Delete-Filter gelten wie überall. `$top` ist auf 1000 begrenzt (`Coworkee:OData:MaxTop`); ungültige Abfragen beantworten `400` mit Problem Details.

## Facetten

Markiere die Eigenschaften, nach denen Benutzer filtern sollen:

```csharp
using Nextended.Core.Facets;

public sealed class Product : AuditedEntity, IMultiTenant
{
    [ProvideFacet(Label = "Brand", ValuePath = "BrandId", LabelPath = "Brand.Name", ValueType = typeof(Guid))]
    public Guid BrandId { get; set; }

    public Brand? Brand { get; set; }
}
```

`LabelPath` ist ein Dynamic-LINQ-Ausdruck. Eine Navigation, die wie eines seiner Schlüsselwörter heißt (`Parent`, `It`, `Root`, `New`, ...), braucht das Präfix `it.`: `LabelPath = "it.Parent.Name"`; ohne scheitert die Facettenabfrage. `coworkee entity add` schreibt immer `it.`.

Fragt der Request mit `Prefer: odata.include-annotations="cn.facets"` danach, enthält die Seite die Facettengruppen unter `@cn.facets`: jede Option mit Beschriftung, Anzahl und dem OData-Filterfragment, das sie setzt. Die Zahlen folgen dem aktuellen Filter, wie man es von Shop-Filtern kennt. Enum-Optionen tragen den Membernamen als Wert und Filter (`Status eq 'Shipped'`) und ihren `[Display]`-Namen als Beschriftung; `[Flags]`-Enums listen jedes Flag einzeln (`Options has 'Express'`). Zahlenbereiche und Datumswerte kommen mit kleinstem und größtem Wert und einem Histogramm. `[FacetBehavior]` neben `[ProvideFacet]` oder `services.ConfigureFacet<T>(...)` legt die Ansicht fest und wie Werte verknüpft werden (*eines von*, *alle von*, einzeln, mit Ausschluss). Mit dem Header `Coworkee-Facets` zählt eine Gruppe ohne ihre eigene Auswahl. [`CoworkeeDataTable`](../ui/data-table.md#facetten) zeigt sie in Ansichten passend zu ihren Werten.

![Marken-Facette mit Anzahlen](../assets/screenshots/products.png){ .shot }

## Regeln pro Zeile

Manche Zeilen sind nicht für alle. Ein Entity-Filter schränkt Listen, Zähler, Facetten und Einzelabfragen gleichermaßen ein:

```csharp
internal sealed class DocumentODataFilter(DocumentVisibility visibility) : IODataEntityFilter<Document>
{
    public Task<IQueryable<Document>> ApplyAsync(IQueryable<Document> query, CancellationToken cancellationToken) =>
        visibility.VisibleAsync(query, cancellationToken);
}

services.AddScoped<IODataEntityFilter<Document>, DocumentODataFilter>();
```

### Kind-Zeilen folgen ihrem Elternteil

Zeilen, die zu einem Elternteil gehören (Punkte eines Gesprächs, Positionen eines Auftrags), sollen genau dann sichtbar sein, wenn das Elternteil es ist. Statt den Filter des Elternteils zu wiederholen:

```csharp
services.AddODataChildFilter<TalkItem, Talk>(i => i.Talk);
```

Ein Punkt wird gezeigt, wenn die sichtbaren Gespräche (alle `IODataEntityFilter<Talk>`, Mandant und Soft Delete, so wie `ODataEntityAccess.VisibleAsync<Talk>` sie liefert) sein Gespräch enthalten; die Abfrage ist eine Unterabfrage auf den Fremdschlüssel, kein Join. Punkte ohne Gespräch sind ausgeblendet. Weitere Filter des Kinds gelten zusätzlich.

### Navigationen folgen ihrem Ziel

Über Navigationen erreicht eine Abfrage andere Zeilen: `$expand`, `$filter=Invoices/any(...)`, `$orderby=Customer/Name`, verschachteltes `$count`, Filter von Export und `query_data`, Berichtsfelder. Sie zeigen genau das, was das eigene Set des Ziels dem Benutzer auflistet:

- eine Sammlung enthält nur die Zeilen, die die `IODataEntityFilter<T>` des Ziels durchlassen, und `any`, `all` und `$count` sehen nur diese;
- eine einzelne Navigation (`Customer`) ist `null`, wenn ihre Zeile ausgeblendet ist;
- `$expand` in ein Set, dessen Berechtigung dem Benutzer fehlt, antwortet `403` mit dem Namen der Navigation; in einem `$filter` hat dieses Set keine Zeilen;
- geschützte Eigenschaften (`HideUnless`) des Ziels bleiben ausgeblendet, auch über die Navigation;
- ein Typ mit Zeilenfiltern, aber ohne eigenes Set, bekommt seine Filter ebenfalls;
- eine Navigation zu einem Basistyp (`Invoice.Shipment` vom Typ `ShipmentBase`) wendet die Filter und Berechtigungen jedes abgeleiteten Sets auf ihre Zeilen an.

Die Filter wirken innerhalb der Abfrage, deshalb müssen sie einfache `Where`-Aufrufe auf der übergebenen Abfrage sein (Closures mit Ids, `EF.Parameter`, Unterabfragen und `AddODataChildFilter` gehen). Ein Filter, der mehr tut (`OrderBy`, `Take`, `Join`, eine neue Abfrage), lässt sich nicht auf eine Navigation anwenden: `$expand` in seinen Typ antwortet `400`, und in einem `$filter` hat der Typ keine Zeilen. Die eigene Liste des Sets nutzt den Filter weiter so, wie er ist. EF-Abfragefilter des DbContext (Mandant, Soft Delete) gelten für Navigationen wie immer.

`ODataNavigationScope.ForAsync(services, typeof(T), ct)` gibt eigenen Abfragen dieselben Regeln: `scope.Apply(query)` schränkt jede Navigation darin ein, außerhalb der Zeilen, die `ODataEntityAccess.VisibleAsync` geliefert hat.

## Eigenschaften ausblenden

Interne Felder bleiben aus dem Modell, sie werden weder ausgeliefert noch sind sie filterbar:

```csharp
services.AddODataEntity<Document>("Documents", DocumentPermissions.Documents.View, d => d.BlobKey);
```

`Hide` und `HideUnless` nehmen auch Member, die eine Basisklasse deklariert (`o => o.Hide(s => s.Tags)` mit `Tags` auf `ShipmentBase`). Meist gibt das EDM jedem Set eine eigene Kopie der Member der Basis, und sie verschwinden nur aus diesem Set. Zeigt eine Navigation auf die Basisklasse (`Invoice.Shipment` vom Typ `ShipmentBase`), hat das EDM die Basis als eigenen Typ, den alle abgeleiteten Sets teilen, und ein ausgeblendeter Member kann ihn nicht für ein Set allein verlassen. Er bleibt dann im Modell, und auf dem ausblendenden Set wird er für alle abgewiesen wie eine `HideUnless`-Eigenschaft, deren Berechtigung niemand hat: nicht serialisiert, `$filter`, `$orderby`, `$select` und `$expand` mit seinem Namen antworten `403`, Exporte, Berichte und Suche lassen ihn weg. Andere Sets der Basis behalten ihn. Die App protokolliert beim Start eine Warnung. Zeilen, die über eine Navigation zur Basis erreicht werden, lassen die ausgeblendeten Eigenschaften ihres eigenen Sets weg; eine expandierte Navigationseigenschaft einer solchen Zeile ist nicht abgedeckt, also die ausgeblendete Navigation eines Sets nicht über ein anderes Set erreichen.

### Eigenschaften hinter einer Berechtigung

Manche Felder sind nur für manche Benutzer, Gehälter etwa für die Personalabteilung. `HideUnless` behält eine Eigenschaft im Modell, hält sie aber von Benutzern ohne die Berechtigung fern:

```csharp
services.AddODataEntity<Employee>("Employees", HrPermissions.Employees.View, o => o
    .Hide(e => e.PasswordResetToken)
    .HideUnless(e => e.Salary, HrPermissions.Salaries)
    .HideUnless(e => e.BirthDate, HrPermissions.PersonalData));
```

Für einen Benutzer ohne die Berechtigung:

- Listen und Einzelabrufe lassen die Eigenschaft weg (im DTO des Clients nullable machen, damit sie leer ankommt und nicht als `0`);
- `$filter`, `$orderby`, `$select`, `$expand`, `$apply`, `$compute` und Facettenfilter, die sie nennen, antworten mit `403`, auch über eine Navigation (`Manager/Salary`);
- ihre Facettengruppe fehlt;
- Exporte lassen die Spalte weg und lehnen Filter darauf ab;
- Berichte bieten das Feld nicht an und gruppieren, aggregieren und filtern nicht danach;
- die globale Suche findet nichts über sie (eigene `Title`- oder `Subtitle`-Funktionen dürfen sie auch nicht zeigen);
- `list_data_sets` und `query_data` des Assistenten listen, liefern und filtern sie nicht;
- die Entity-Historie lässt ihre Änderungen weg, und eine Wiederherstellung fasst sie nicht an;
- Webhooks eines Endpunkts, dessen Besitzer die Berechtigung fehlt, lassen sie weg.

Geprüft wird pro Berechtigung, nicht pro Zeile. Das Audit-Log für Admins (`Audit.View`) zeigt weiterhin jede Änderung.

## Aus dem Blazor-Client

`IODataClient` baut die URL und liest Werte, Anzahl und Facetten:

```csharp
var page = await odata.QueryAsync<BrandDto>("Brands", new ODataQuery { OrderBy = "Name", Top = 1000, Count = false });
```

Der BFF leitet `/odata` mit dem Token des Benutzers an die API weiter.

## Client-Entities

Markiere eine Entity mit `[ClientEntity]`, und Blazor-Clients halten eine Kopie ihres Sets und fragen sie lokal ab; siehe [Client-Entities](../ui/client-entities.md).

## Export und Import

Jedes Entity-Set lässt sich mit `$filter`, `$search` und `$orderby` der Liste als Excel, CSV oder JSON exportieren; `AddODataImport` macht ein Set über einen Assistenten importierbar, mit Spaltenzuordnung, Vorschau, Upsert, Probelauf, Hintergrundjobs und Rückgängig:

```http
GET /api/v1/data/Products/export?format=csv&$filter=Rate gt 10&$orderby=Name
```

```csharp
services.AddODataImport("Brands", (Guid? id, AddEditBrandRequest row) => new AddEditBrandCommand(id, row));
```

![Import-Ergebnis](../assets/screenshots/import.png){ .shot }

Alles Weitere steht unter [Import und Export](import-export.md).
