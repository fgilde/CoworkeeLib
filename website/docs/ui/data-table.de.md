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

`ReloadAsync()` lädt von außen neu, etwa aus einer `RealtimeSubscription`. `Selection` liefert die Facettenauswahl, `CurrentFilter` den OData-Filter, den die Tabelle sendet.

Der CSV-Export entschärft Werte, die Tabellenkalkulationen sonst als Formel ausführen würden.

## Bearbeitungsdialoge

Für einfache Modelle reicht ein erzeugter Dialog:

```csharp
if (await Dialogs.ShowEditAsync("Edit brand", model) is { } saved)
{
    await Snackbar.RunAsync(() => Api.SaveBrandAsync(id, saved), "Brand saved");
}
```

Mit `meta => ...` konfigurieren Sie das `MudExObjectEditForm` (Beschriftungen, Reihenfolge, Editoren). Für Nachschlagelisten, Uploads oder Eigenes schreiben Sie eine Dialogkomponente, wie das Template mit `ProductDialog` und `DocumentDialog`.
