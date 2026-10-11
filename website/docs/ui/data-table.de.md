# Datentabellen

`CoworkeeDataTable<T>` ist ein MudBlazor-Datagrid auf einem [OData-Set](../web/odata.md). Der Server blättert, sortiert, filtert und zählt; die Tabelle ergänzt Suche, Facetten, Export, Auswahl und die Schaltflächen zum Anlegen, Bearbeiten und Löschen mit ihren Berechtigungen.

```razor
<CoworkeeDataTable T="ProductDto" EntitySet="Products" Expand="Brand" SearchFields="SearchFields" MultiSelection="true" ExportFileName="products"
                   CreatePermission="@CatalogPermissions.Products.Create" EditPermission="@CatalogPermissions.Products.Edit" DeletePermission="@CatalogPermissions.Products.Delete"
                   OnCreate="CreateAsync" OnEdit="EditAsync" OnDelete="DeleteAsync" DescribeItem="p => p.Name">
    <Columns>
        <PropertyColumn Property="p => p.Name" />
        <PropertyColumn Property="p => p.Brand!.Name" Title="Brand" />
        <PropertyColumn Property="p => p.Rate" Format="0.00" />
    </Columns>
</CoworkeeDataTable>
```

![Produkte mit geöffneter Marken-Facette](../assets/screenshots/products.png){ .shot }

| Parameter | |
|---|---|
| `EntitySet` | Name des OData-Sets |
| `Columns` | MudBlazor-Spalten; verschachtelte Eigenschaften sortieren als `Brand/Name` |
| `Expand` | zu ladende Navigationseigenschaften, z. B. `"Brand"` |
| `Filter` | ein fester OData-Filter zusätzlich zur Auswahl des Benutzers |
| `SearchFields` | Eigenschaften, in denen das Suchfeld sucht (`contains`, ohne Groß-/Kleinschreibung) |
| `Facets` | Facettenmenüs anzeigen (Standard `true`) |
| `MultiSelection` | Checkboxen und eine Schaltfläche *Delete n* |
| `OnCreate`, `OnEdit`, `OnDelete` | Callbacks; die Tabelle lädt danach jeweils neu |
| `CreatePermission`, `EditPermission`, `DeletePermission` | blenden die Schaltflächen ohne Berechtigung aus |
| `DescribeItem` | Text für die Löschbestätigung |
| `Exportable`, `ExportFileName` | CSV- und JSON-Export aller Zeilen des aktuellen Filters |
| `ToolBarContent` | zusätzliche Schaltflächen in der Werkzeugleiste |
| `PageSize` | Zeilen pro Seite (25) |
| `Groupable`, `ReorderableColumns`, `ResizableColumns`, `DensityToggle` | Gruppierung, Spalten per Drag & Drop verschieben, Spaltenbreite ziehen und die Dichte-Schaltfläche (alle `true`) |
| `DataSource` | Zeilen aus einer anderen Quelle als dem OData-Set, siehe unten |
| `ColumnFilters` | ein Filter in jedem Spaltenkopf, siehe [Spaltenfilter](#spaltenfilter) (Standard `options.DataTable.ColumnFilters`, `true`) |

Die Tabelle lädt von selbst neu, wenn ihre `[Realtime]`-Entity sich ändert (`RealtimeTopic`, siehe [Realtime](../modules/realtime.md#anderungen-an-entities)). `ReloadAsync()` lädt von außen neu, etwa aus einer `RealtimeSubscription`. `Selection` liefert die Facettenauswahl, `CurrentFilter` den OData-Filter, den die Tabelle sendet (fester Filter, Facetten, Spaltenfilter und Suche).

Für Sets, die als [Client-Entities](client-entities.md) markiert sind, filtert, sucht, sortiert und zählt die Tabelle im Browser, sobald die Zeilen angekommen sind.

Der CSV-Export entschärft Werte, die Tabellenkalkulationen sonst als Formel ausführen würden.

## Spaltenfilter

Wie in QuickGrid lässt sich jede Spalte filtern: das Filtersymbol im Spaltenkopf (oder *Filter* im Spaltenmenü) öffnet einen kleinen Editor passend zum Typ der Spalte. Ein ausgefülltes Symbol zeigt, dass die Spalte gefiltert ist, und jeder Filter erscheint als Chip über der Tabelle; sein &times; entfernt ihn, *Alle entfernen* entfernt Filter und Facetten zusammen.

| Spaltentyp | Editor | OData |
|---|---|---|
| Text | *enthält*, *beginnt mit*, *ist gleich* und ein Wert | `contains(tolower(Name),'dri')`, `startswith(...)`, `Name eq 'Drill'` |
| Zahlen | *=*, *>*, *<*, *zwischen* | `Rate gt 3`, `Rate ge 3 and Rate le 10` |
| Datum | *ist*, *nach*, *vor*, *zwischen* (ganze Tage) | `Added ge 2026-02-01T00:00:00+01:00 and Added lt ...` |
| Enums, Ja/Nein | Checkboxen, mehrere sind Alternativen | `(Kind eq 'Hand' or Kind eq 'Power')` |
| eine Spalte mit Facette | die Optionen der Facette mit ihren Anzahlen | der Filter der Facette |

**Facetten und Spaltenfilter sind dasselbe.** Hat das Set eine Facette auf der Eigenschaft der Spalte, zeigt der Spaltenfilter deren Optionen und Anzahlen; ein Haken dort oder im Facettenmenü ist dieselbe Auswahl (derselbe Chip, dieselbe URL). Eine Spalte auf einer Navigationseigenschaft nutzt die Facette ihres Schlüssels: `Brand.Name` nutzt eine Facette auf `BrandId`.

Die Filter werden mit `and` an den OData-Filter der Tabelle gehängt. Der Server beantwortet sie, bei [Client-Entities](client-entities.md) auch der Browser: sie nutzen nur, was beide verstehen (`eq`, `ne`, `gt`, `ge`, `lt`, `le`, `and`, `or`, `not`, `contains`, `startswith`, `endswith`, `tolower`). Sie gehören zum Zustand der Tabelle: gespeicherte Ansichten, geteilte Links und die Adresszeile behalten sie.

### Ab- und einschalten

Spaltenfilter sind standardmäßig an. Jede Ebene kann das ändern:

```razor
@* die ganze Tabelle *@
<CoworkeeDataTable T="ProductDto" EntitySet="Products" ColumnFilters="false" ...>
    <Columns>
        @* nur diese Spalte, obwohl die Tabelle sie abschaltet *@
        <PropertyColumn Property="p => p.Name" Filterable="true" />
        @* diese Spalte nie *@
        <PropertyColumn Property="p => p.Description" Filterable="false" />
        @* nur bestimmte Operatoren *@
        <PropertyColumn Property="p => p.Barcode" FilterOperators="@(new() { FilterOperator.String.Equal })" />
    </Columns>
</CoworkeeDataTable>
```

Template-Spalten haben keine Eigenschaft und sind nicht filterbar. Für die ganze App, je Typ:

```csharp
builder.Services.AddCoworkeeClient(new Uri(builder.HostEnvironment.BaseAddress), options =>
{
    options.DataTable.ColumnFilters = true;   // Standard jeder Tabelle
    options.DataTable.FilterOperators<DateTime>(FilterOperator.DateTime.After, FilterOperator.DateTime.Before, DataTableFilters.Between);
    options.DataTable.FilterOperators<Guid>(); // keine Operatoren: Guid-Spalten sind nicht filterbar
});
```

Die Reihenfolge: `Filterable`/`FilterOperators` der Spalte, dann die Optionen für den Typ, dann die Standards oben.

### Eigener Editor

`FilterTemplate` an einer Spalte ersetzt den Editor. Er bekommt MudBlazors `FilterContext`; Filterdefinitionen mit der Spalte und einem von MudBlazors `FilterOperator`s macht die Tabelle zu OData:

```razor
<PropertyColumn Property="p => p.Rate" Title="Rate">
    <FilterTemplate>
        <MudButton OnClick="@(() => context.Actions.ApplyFilterAsync(new FilterDefinition<ProductDto>
            { Id = Guid.NewGuid(), Column = context.FilterDefinition!.Column, Operator = FilterOperator.Number.GreaterThanOrEqual, Value = 4m }))">
            Ab Rate 4
        </MudButton>
    </FilterTemplate>
</PropertyColumn>
```

`DataTableFilters.ToOData(...)` macht aus solchen Bedingungen selbst OData-Text, etwa für eine `ITableDataSource`.

**Grenzen**: ein Editor pro Spalte (zwei Filter für *zwischen*); der Filter gilt der Eigenschaft der Spalte, nicht dem, was ein `CellTemplate` anzeigt. Datumswerte werden nach ganzen Tagen in der Zeitzone des Browsers gefiltert.

## Bearbeitungsdialoge

Für einfache Modelle reicht ein erzeugter Dialog:

```csharp
if (await Dialogs.ShowEditAsync("Edit brand", model) is { } saved)
{
    await Snackbar.RunAsync(() => Api.SaveBrandAsync(id, saved), "Brand saved");
}
```

Bearbeitungsdialoge sind Seitenblätter in voller Höhe, die von der Seite hereingleiten, auf die der Benutzer geklickt hat, auf breiten Bildschirmen zweispaltig. Mit `meta => ...` konfigurieren Sie das `MudExObjectEditForm` (Beschriftungen, Reihenfolge, Editoren). Mit einer Speicherfunktion bleibt der Dialog offen und zeigt die Meldungen der API, wenn das Speichern scheitert:

![Bearbeitungsdialog](../assets/screenshots/edit-sheet.png){ .shot }

```csharp
await Dialogs.ShowEditAsync(L["New product"], model, saved => Api.SaveProductAsync(id, saved), meta =>
{
    meta.Property(p => p.BrandId).WithLabel(L["Brand"])
        .RenderWith<ODataPicker, Guid>(p => p.Value)
        .WithAdditionalAttribute(nameof(ODataPicker.EntitySet), "Brands");
    meta.Property(p => p.ImageDataUrl).WithLabel(L["Image"]).RenderWith<ImageDataUrlEdit, string?>(p => p.Value);
});
```

| Editor | für |
|---|---|
| `ODataPicker` | einen Fremdschlüssel: sucht in einem OData-Entity-Set über eine Texteigenschaft |
| `ImageDataUrlEdit` | ein Bild, gespeichert als Data-URL |

`ShowSideSheetAsync<TDialog>` öffnet eine eigene Dialogkomponente genauso, `ConfirmAsync` fragt vor etwas, das sich nicht rückgängig machen lässt.

## Spalten, Gruppierung und Ansichten

Alles, was der Benutzer einrichtet, gehört zum Zustand der Tabelle und lässt sich speichern, teilen und zurücksetzen.

- **Spalten**: das Spalten-Menü blendet Spalten ein und aus (mit *Alle*, *Keine* und Suche) und sortiert sie per Drag & Drop um. Auch die Spaltenköpfe lassen sich an eine andere Stelle ziehen, ihr rechter Rand ändert die Breite.
- **Gruppierung**: einen Spaltenkopf auf die Zone über den Köpfen ziehen, *Gruppieren* im Menü der Spalte wählen oder Spalten im Gruppier-Menü ankreuzen. Mehrere Spalten gruppieren verschachtelt, in der Reihenfolge der Chips; das &times; eines Chips entfernt seine Ebene. Gruppenzeilen zeigen den Wert und die Anzahl der Zeilen, klappen einzeln oder alle auf und zu und zeigen unter jeder Gruppe die Aggregate der Spalten (`AggregateDefinition`, z. B. einen Durchschnitt).
- **Sortierung**: mehrere Spaltenköpfe anklicken sortiert nach mehreren Spalten.
- **Dichte**: die Dichte-Schaltfläche wechselt zwischen kompakten und großzügigen Zeilen; ohne Wahl entscheidet das Theme.
- **Ansichten**: das Lesezeichen-Menü speichert den aktuellen Zustand unter einem Namen: Suche, Facetten, Spaltenfilter, Reihenfolge, Sichtbarkeit und Breite der Spalten, Gruppierung, Sortierung, Seitengröße und Dichte. Ein Klick stellt ihn wieder her, die Speichern-Schaltfläche überschreibt ihn mit dem aktuellen Zustand, der Stern macht ihn zum Standard, der mit der Tabelle öffnet. *Zurücksetzen* kehrt zu den deklarierten Spalten ohne Filter zurück.
- **Teilen**: die Link-Schaltfläche kopiert eine Adresse, die die Tabelle genau so öffnet, samt Layout. Die Adresszeile selbst hält Suche, Facetten und Spaltenfilter (`?products=...`), eine gefilterte Liste lässt sich also neu laden und als Lesezeichen speichern. `UrlState="false"` schaltet das ab, `StateKey` benennt den Parameter, wenn eine Seite zwei Tabellen desselben Sets zeigt.

```razor
<CoworkeeDataTable T="ProductDto" EntitySet="Products" Expand="Brand" ...>
    <Columns>
        <PropertyColumn Property="p => p.Name" />
        <PropertyColumn Property="p => p.Brand!.Name" Title="Brand" />
        <PropertyColumn Property="p => p.Rate" Format="0.00" AggregateDefinition="AverageRate" />
    </Columns>
</CoworkeeDataTable>

@code {
    private static readonly AggregateDefinition<ProductDto> AverageRate = new() { Type = AggregateType.Avg, DisplayFormat = "Ø {value}", NumberFormat = "0.00" };
}
```

Spalten heißen nach ihrer Eigenschaft (`Brand.Name`), Template-Spalten nach ihrem `Title`; Spalten ohne beides (Auswahl, Aktionen) bleiben, wo sie sind. Template-Spalten gruppieren nur mit `Groupable="true"` und einem `GroupBy`.

**Gruppierung und Seiten**: der Server blättert, eine Seite gruppiert also die Zeilen, die sie enthält. Die Tabelle sortiert zuerst nach den Gruppierspalten, damit eine Gruppe auf der nächsten Seite weitergeht statt verstreut zu werden; Anzahl und Aggregate einer Gruppe beziehen sich auf die geladene Seite.

**Wo Ansichten liegen**: in der Benutzereinstellung `Table.Views` des [Einstellungsmoduls](../modules/settings.md), sie folgen dem Benutzer also auf jedes Gerät. Ohne angemeldeten Benutzer oder ohne Einstellungsmodul bleiben sie im Local Storage des Browsers. Wer sie woanders ablegen will, ersetzt `ITableViewStore` in DI:

```csharp
builder.Services.AddScoped<ITableViewStore, MyTableViews>();
```

`CurrentState` liefert den Zustand als `DataTableState` (nur, was vom deklarierten Layout abweicht), zum Beispiel für eigene Links; `DataTableState.Encode()` ergibt den Wert für den Query-Parameter.

## Andere Datenquellen

Standardmäßig fragt die Tabelle `EntitySet` über OData ab. `DataSource` nimmt stattdessen eine beliebige `ITableDataSource<T>`, zum Beispiel Zeilen, die im Browser liegen:

```csharp
public sealed class LocalProducts(ProductStore store) : ITableDataSource<ProductDto>
{
    public Task<ODataPage<ProductDto>> QueryAsync(ODataQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Query(query));
}
```

Die Abfrage trägt den OData-Text, den die Tabelle baut (Filter aus Suche und Facetten, Sortierung, `Top`/`Skip`), die Quelle beantwortet also diese Teilmenge. `EntitySet` benennt weiterhin Ansichten und URL-Parameter. CSV- und JSON-Export nutzen die Quelle; Excel-Export und -Import bleiben bei OData-Sets.
