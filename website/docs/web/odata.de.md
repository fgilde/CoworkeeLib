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

### Schlüssel außer `Id`

Ein Set mit einer anderen Schlüsseleigenschaft, von beliebigem primitivem Typ, nennt sie mit `Key`:

```csharp
services.AddODataEntity<Country>("Countries", MasterDataPermissions.View, o => o.Key(c => c.IsoAlpha2Code));
```

`GET /odata/Countries('DE')` liest eine Zeile (`/odata/Ranks(2)` bei einem Zahlenschlüssel). Listen, `$count`, Facetten, die globale Suche (die Id des Treffers ist der Schlüssel), Exporte, Berichte und `query_data` funktionieren wie bei jedem Set. Funktionen, die Zeilen über eine Guid-`Id` ansprechen, weisen eine Entity ohne sie schon bei der Registrierung ab: Datensatz-Anhänge, Social (Kommentare, Tags, Bewertungen, Reaktionen), Workflows, Entity-Historie und Webhook-Entity-Ereignisse. Client-Entities lassen solche Sets auf dem Server. Importe, die aktualisieren oder upserten (`AddODataImport` mit Id), finden Zeilen über den Schlüssel des Sets, der eine Guid sein muss; bei anderen Schlüsseln den Import ohne Id registrieren (nur Anlegen), sonst wirft die Registrierung. `AddExtendedAttributes` weist eine Entity ohne Guid-`Id` ab. Eine Entity, die neben einem anderen OData-Schlüssel eine Guid-`Id` behält, hat sie alle weiterhin.

Namen in der URI passen unabhängig von Groß- und Kleinschreibung, ein camelCase-Client darf also `$filter=name eq 'x'`, `$orderby=price` oder `$expand=brand` senden; `Coworkee:OData:CaseInsensitive` (Standard `true`) schaltet das ab. Antworten behalten die Namen der C#-Eigenschaften (`Name`); ein Client, der camelCase-JSON möchte, bildet sie selbst ab. Abfrageoptionen brauchen ihr `$`: `filter=` wird nicht gelesen. Eine Antwort `400` erklärt ODatas eigene Fehler in `detail`; andere Fehler einer Abfrage (etwa wenn EF sie nicht übersetzen kann) kommen ohne Detail und werden protokolliert, weil ihre Meldungen Zeilenfilter zitieren können.

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

Über Navigationen erreicht eine Abfrage andere Zeilen: `$expand` (auch `[AutoExpand]`-Navigationen, die ohne es expandieren), `$filter=Invoices/any(...)`, `$orderby=Customer/Name`, verschachteltes `$count`, `$compute`, `/$count`, Facetten und ihre Beschriftungen, Filter von Export und `query_data`, Felder der globalen Suche, Berichtsfelder. Sie zeigen genau das, was das eigene Set des Ziels dem Benutzer auflistet:

- eine Sammlung enthält nur die Zeilen, die die `IODataEntityFilter<T>` des Ziels durchlassen, und `any`, `all` und `$count` sehen nur diese;
- eine einzelne Navigation (`Customer`) ist `null`, wenn ihre Zeile ausgeblendet ist, auf jeder Ebene (`Invoice/Shipment/Customer`), und eine Sammlung unter einer ausgeblendeten Referenz ist leer;
- `$expand` in ein Set, dessen Berechtigung dem Benutzer fehlt, antwortet `403` mit dem Namen der Navigation; in einem `$filter` und hinter einer `[AutoExpand]`-Navigation hat dieses Set keine Zeilen;
- geschützte Eigenschaften (`HideUnless`), ausgeblendete Member einer geteilten Basis und berechnete Eigenschaften mit `Permission` des Ziels bleiben ausgeblendet, auch über die Navigation: wer sie nennt, bekommt `403`, expandierte Zeilen lassen sie weg;
- ein Typ mit Zeilenfiltern, aber ohne eigenes Set, bekommt seine Filter ebenfalls;
- eine Navigation zu einem Basistyp (`Invoice.Shipment` vom Typ `ShipmentBase`) wendet die Filter und Berechtigungen jedes abgeleiteten Sets auf ihre Zeilen an;
- Facetten zählen die eingeschränkten Zeilen, und eine Facettenbeschriftung über eine Navigation (`[ProvideFacet(LabelPath = "it.Customer.Name")]`) zeigt den Schlüssel statt des Namens, wenn der Benutzer die Zeile nicht sehen darf.

Die Filter wirken innerhalb der Abfrage, deshalb müssen sie einfache `Where`-Aufrufe auf der übergebenen Abfrage sein (Closures mit Ids, `EF.Parameter`, Unterabfragen und `AddODataChildFilter` gehen). Ein Filter, der mehr tut (`OrderBy`, `Take`, `Join`, eine neue Abfrage) oder auf der leeren In-Memory-Abfrage scheitert, aus der er gelesen wird (einer, der zuerst die Datenbank fragt), lässt sich nicht auf eine Navigation anwenden: `$expand` in seinen Typ antwortet `400`, und in einem `$filter` hat der Typ keine Zeilen. Andere Abfragen trifft das nicht: geprüft werden nur die Ziele, die eine Abfrage wirklich erreicht, einmal pro Anfrage. Die eigene Liste des Sets nutzt den Filter weiter so, wie er ist. EF-Abfragefilter des DbContext (Mandant, Soft Delete) gelten für Navigationen wie immer.

Die Prüfungen laufen auf der Abfrage, die OData bindet, nicht auf ihrem Text: jede Eigenschaft und Navigation, die `$filter`, `$orderby`, `$compute`, `$apply`, `$select` und `$expand` (auch verschachtelte Optionen) lesen, wird gegen die gesperrten Member des Typs geprüft, von dem sie gelesen wird, seiner Basen und, über einen Basistyp gelesen, jedes abgeleiteten Sets. `?+$filter=`, `%20$expand=` und `Featured/ Size` werden also wie jede andere Schreibweise geprüft, und ein Literal wie `'a/Note'` ist keine Eigenschaft. Member werden geprüft, gleich welcher Knoten sie liest, Aggregate über Entity-Sets (`aggregate(Employees(Salary with max as M))`) und in `$expand` verschachtelte Optionen eingeschlossen, und eine Abfrage, die die Prüfung nicht parsen kann, antwortet `400`, statt zu laufen. `$search` sucht nur in den Text-Eigenschaften, die der Benutzer lesen darf (kein `Hide`, kein `HideUnless` ohne die Berechtigung), in Listen und Exporten; ein Suchfeld, das eine `ISearchable`-Entity nennt (`x => x.FirstName + " " + x.LastName`, `x => x.Note.ToLower()`), fällt weg, wenn ein Member, den es liest, ausgeblendet oder gesperrt ist oder über eine Navigation erreicht wird, deren Ziel Zeilenfilter oder eine fehlende Berechtigung hat. Parameter-Aliase (`$filter=@a gt 5&@a=Size`) werden mit `400` abgewiesen, ebenso `$apply` über eine Navigation zu Zeilen mit Zeilenfiltern oder Berechtigungen (ein ausgeblendetes Ziel ergäbe eine Gruppe leerer Werte); `$apply` über die eigenen Eigenschaften des Sets oder über Navigationen ohne solche Regeln funktioniert. Eine Facette, deren Wert über eine Navigation gelesen wird (`ValuePath = "it.Customer.Name"`), fällt weg, wenn ihr Ziel Zeilenfilter oder eine Berechtigung hat, die dem Benutzer fehlt, oder der Wert ihm gesperrt ist; der Schlüssel des Ziels (`Customer/Id`) bleibt, wenn er der eigene, gemappte und lesbare Fremdschlüssel der Zeile auf den EF-Schlüssel des Ziels ist, und komplexe Member und Ziele ohne solche Regeln bleiben. Eine Facette über eine Sammlung (`it.Lines.Id`) wird beim Aufbau des Modells abgewiesen. `$expand=*($levels=2)` in ein Set, das der Benutzer nicht lesen darf, kommt leer zurück statt mit `403`, wie eine `[AutoExpand]`-Navigation.

Nicht abgedeckt: eigener Code, der ohne den Scope Navigationen auf `ODataEntityAccess.VisibleAsync` setzt, und die Funktionen `Title`, `Subtitle`, `Url` und `Thumbnail` der globalen Suche (sie sehen nur die eigenen Spalten der Zeile). `ODataNavigationScope.For(services, typeof(T))` gibt eigenen Abfragen dieselben Regeln: `await scope.ApplyAsync(query, ct)` schränkt jede Navigation darin ein, außerhalb der Zeilen, die `VisibleAsync` geliefert hat.

## Berechnete Eigenschaften

Ein Wert, der aus anderen Spalten oder anderen Tabellen berechnet wird, ist eine `ODataComputedProperty` des Sets. Die Datenbank wertet sie aus, deshalb funktionieren `$filter`, `$orderby` und `$select` auf ihr; zurück kommt sie nur, wenn `$select` sie nennt:

```csharp
internal sealed class ShipmentProperties : IODataComputedPropertySource
{
    private static readonly Expression<Func<CalculatedShipment, string>> Display = s => s.ShipmentNumber + " / " + s.HouseBill;

    public IEnumerable<ODataComputedProperty> Properties(ODataEntityRegistry registry) =>
        [new(typeof(CalculatedShipment), "ReferenceNumberDisplay", typeof(string), row => ODataComputedProperty.Inline(Display, row))];
}

services.AddSingleton<IODataComputedPropertySource, ShipmentProperties>();
```

```http
GET /odata/CalculatedShipments?$select=Id,ReferenceNumberDisplay&$orderby=ReferenceNumberDisplay&$filter=contains(ReferenceNumberDisplay, 'HB-1')
```

Ein Ausdruck über die eigenen Spalten der Zeile funktioniert auf jedem Set, auch auf einem einer [externen Datenbank](../fundamentals/persistence.md#externe-datenbanken). Eine Unterabfrage auf eine andere Tabelle nutzt `ComputedQueryRoot.Of<T>()` als Quelle; das braucht den eigenen DbContext der App. `Permission` blendet die Eigenschaft für Benutzer ohne die Berechtigung aus, `Exported` macht sie zur Exportspalte, `Facet` gibt ihr eine Facettengruppe. Eine C#-Eigenschaft nur mit Getter (`public string Display => ...`) ist keine Spalte; das OData-Modell lässt sie weg, also so einen Wert auf diesem Weg anbieten.

## Eigenschaften ausblenden

Interne Felder bleiben aus dem Modell, sie werden weder ausgeliefert noch sind sie filterbar:

```csharp
services.AddODataEntity<Document>("Documents", DocumentPermissions.Documents.View, d => d.BlobKey);
```

`Hide` und `HideUnless` nehmen auch Member, die eine Basisklasse deklariert (`o => o.Hide(s => s.Tags)` mit `Tags` auf `ShipmentBase`). Meist gibt das EDM jedem Set eine eigene Kopie der Member der Basis, und sie verschwinden nur aus diesem Set. Zeigt eine Navigation auf die Basisklasse (`Invoice.Shipment` vom Typ `ShipmentBase`), hat das EDM die Basis als eigenen Typ, den alle abgeleiteten Sets teilen, und ein ausgeblendeter Member kann ihn nicht für ein Set allein verlassen. Er bleibt dann im Modell, und auf dem ausblendenden Set wird er für alle abgewiesen wie eine `HideUnless`-Eigenschaft, deren Berechtigung niemand hat: nicht serialisiert, `$filter`, `$orderby`, `$select` und `$expand` mit seinem Namen antworten `403`, Exporte, Berichte und Suche lassen ihn weg. Andere Sets der Basis behalten ihn. Die App protokolliert beim Start eine Warnung. Zeilen, die über eine Navigation zur Basis erreicht werden, lassen die ausgeblendeten Eigenschaften und Navigationen ihres eigenen Sets weg (`Stickers?$expand=Owner($expand=Stickers)` hat keine Sticker an Besitzern, deren Set sie ausblendet).

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
