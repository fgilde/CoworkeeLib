# Data tables

`CoworkeeDataTable<T>` is a MudBlazor data grid on top of an [OData set](../web/odata.md). The server pages, sorts, filters and counts; the table adds search, facets, export, selection and the create, edit and delete buttons with their permissions.

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

![Products with the brand facet open](../assets/screenshots/products.png){ .shot }

| Parameter | |
|---|---|
| `EntitySet` | name of the OData set |
| `Columns` | MudBlazor columns; nested properties sort as `Brand/Name` |
| `Expand` | navigation properties to load, e.g. `"Brand"` |
| `Filter` | a fixed OData filter on top of the user's choices |
| `SearchFields` | properties the search box looks in (case insensitive `contains`) |
| `Facets` | show the facet menus (default `true`) |
| `MultiSelection` | check boxes and a *Delete n* button |
| `OnCreate`, `OnEdit`, `OnDelete` | callbacks; the table reloads after each of them |
| `CreatePermission`, `EditPermission`, `DeletePermission` | hide the buttons without the permission |
| `DescribeItem` | text for the delete confirmation |
| `Exportable`, `ExportFileName` | CSV and JSON export of all rows of the current filter |
| `ToolBarContent` | extra buttons in the tool bar |
| `PageSize` | rows per page (25) |
| `Groupable`, `ReorderableColumns`, `ResizableColumns`, `DensityToggle` | grouping, moving columns by drag and drop, resizing columns and the density button (all `true`) |
| `DataSource` | rows from somewhere else than the OData set, see below |

`ReloadAsync()` reloads from outside, for example from a `RealtimeSubscription`. `Selection` exposes the facet choices, `CurrentFilter` the OData filter the table sends.

The CSV export escapes values that spreadsheet programs would run as formulas.

## Edit dialogs

For plain models a generated dialog is enough:

```csharp
if (await Dialogs.ShowEditAsync("Edit brand", model) is { } saved)
{
    await Snackbar.RunAsync(() => Api.SaveBrandAsync(id, saved), "Brand saved");
}
```

Edit dialogs are full height side sheets that slide in from the side the user clicked on, with two columns on wide screens.

![Edit side sheet](../assets/screenshots/edit-sheet.png){ .shot }
 Pass `meta => ...` to configure the `MudExObjectEditForm` (labels, order, editors). With a save function the dialog stays open and shows the messages of the API when saving fails:

```csharp
await Dialogs.ShowEditAsync(L["New product"], model, saved => Api.SaveProductAsync(id, saved), meta =>
{
    meta.Property(p => p.BrandId).WithLabel(L["Brand"])
        .RenderWith<ODataPicker, Guid>(p => p.Value)
        .WithAdditionalAttribute(nameof(ODataPicker.EntitySet), "Brands");
    meta.Property(p => p.ImageDataUrl).WithLabel(L["Image"]).RenderWith<ImageDataUrlEdit, string?>(p => p.Value);
});
```

| Editor | for |
|---|---|
| `ODataPicker` | a foreign key: searches an OData entity set by a text property |
| `ImageDataUrlEdit` | a picture kept as data URL |

`ShowSideSheetAsync<TDialog>` opens your own dialog component the same way, `ConfirmAsync` asks before something that cannot be undone.

## Columns, grouping and views

Everything the user arranges is part of the table's state and can be saved, shared and reset.

- **Columns**: the column menu shows and hides columns (with *All*, *None* and a search) and reorders them by drag and drop. Column headers can be dragged to another place too, and their right edge resizes the column.
- **Grouping**: drag a column header onto the zone above the headers, use *Group* in the column's own menu or tick columns in the group menu. Several columns group nested, in the order of the chips; a chip's &times; removes its level. Group rows show the value and the number of rows, open and close one by one or all at once, and show the aggregates of the columns (`AggregateDefinition`, e.g. an average) below each group.
- **Sorting**: click several headers to sort by more than one column.
- **Density**: the density button switches between compact and comfortable rows; without a choice the theme decides.
- **Views**: the bookmark menu saves the current state under a name: search, facets, column order, visibility and widths, grouping, sorting, page size and density. A click brings it back, the save button overwrites it with the current state, the star makes it the default that opens with the table. *Reset* returns to the declared columns without filters.
- **Sharing**: the link button copies an address that opens the table exactly as it is, layout included. The address bar itself keeps search and facets (`?products=...`), so a filtered list can be reloaded and bookmarked. `UrlState="false"` turns that off, `StateKey` names the parameter when a page shows two tables of one set.

```razor
<CoworkeeDataTable T="ProductDto" EntitySet="Products" Expand="Brand" ...>
    <Columns>
        <PropertyColumn Property="p => p.Name" />
        <PropertyColumn Property="p => p.Brand!.Name" Title="Brand" />
        <PropertyColumn Property="p => p.Rate" Format="0.00" AggregateDefinition="AverageRate" />
    </Columns>
</CoworkeeDataTable>

@code {
    private static readonly AggregateDefinition<ProductDto> AverageRate = new() { Type = AggregateType.Avg, DisplayFormat = "Ø {value:0.00}" };
}
```

Columns are named by their property (`Brand.Name`), template columns by their `Title`; columns without either (select, actions) stay where they are. Template columns only group with `Groupable="true"` and a `GroupBy`.

**Grouping and paging**: the server pages, so a page groups the rows it holds. The table sorts by the grouping columns first, so a group continues on the next page instead of being scattered; group counts and aggregates cover the loaded page.

**Where views are kept**: in the user setting `Table.Views` of the [settings module](../modules/settings.md), so they follow the user to every device. Without a signed-in user or without the settings module they stay in the browser's local storage. Replace `ITableViewStore` in DI to keep them somewhere else:

```csharp
builder.Services.AddScoped<ITableViewStore, MyTableViews>();
```

`CurrentState` returns the state as a `DataTableState` (only what differs from the declared layout), e.g. to build your own links; `DataTableState.Encode()` gives the value for the query parameter.

## Other data sources

By default the table queries `EntitySet` through OData. `DataSource` takes any `ITableDataSource<T>` instead, for example rows kept in the browser:

```csharp
public sealed class LocalProducts(ProductStore store) : ITableDataSource<ProductDto>
{
    public Task<ODataPage<ProductDto>> QueryAsync(ODataQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Query(query));
}
```

The query carries the OData text the table builds (filter from search and facets, order, `Top`/`Skip`), so the source answers that subset. `EntitySet` still names the views and the URL parameter. CSV and JSON export use the source; Excel export and import stay with OData sets.
