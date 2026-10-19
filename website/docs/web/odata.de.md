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

## Eigenschaften ausblenden

Interne Felder bleiben aus dem Modell, sie werden weder ausgeliefert noch sind sie filterbar:

```csharp
services.AddODataEntity<Document>("Documents", DocumentPermissions.Documents.View, d => d.BlobKey);
```

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
