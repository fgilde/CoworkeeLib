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

`ReloadAsync()` lädt von außen neu, etwa aus einer `RealtimeSubscription`. `Selection` liefert die Facettenauswahl, `CurrentFilter` den OData-Filter, den die Tabelle sendet.

Für Sets, die als [Client-Entities](client-entities.md) markiert sind, filtert, sucht, sortiert und zählt die Tabelle im Browser, sobald die Zeilen angekommen sind.

Der CSV-Export entschärft Werte, die Tabellenkalkulationen sonst als Formel ausführen würden.

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
- **Ansichten**: das Lesezeichen-Menü speichert den aktuellen Zustand unter einem Namen: Suche, Facetten, Reihenfolge, Sichtbarkeit und Breite der Spalten, Gruppierung, Sortierung, Seitengröße und Dichte. Ein Klick stellt ihn wieder her, die Speichern-Schaltfläche überschreibt ihn mit dem aktuellen Zustand, der Stern macht ihn zum Standard, der mit der Tabelle öffnet. *Zurücksetzen* kehrt zu den deklarierten Spalten ohne Filter zurück.
- **Teilen**: die Link-Schaltfläche kopiert eine Adresse, die die Tabelle genau so öffnet, samt Layout. Die Adresszeile selbst hält Suche und Facetten (`?products=...`), eine gefilterte Liste lässt sich also neu laden und als Lesezeichen speichern. `UrlState="false"` schaltet das ab, `StateKey` benennt den Parameter, wenn eine Seite zwei Tabellen desselben Sets zeigt.

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
