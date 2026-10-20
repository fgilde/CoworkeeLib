# OData and facets

Every entity can be an OData set with one line. Tables, exports and external tools read through it; you write no list queries.

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

| Request | Result |
|---|---|
| `GET /odata/Products?$filter=Rate gt 20&$orderby=Name&$top=25&$skip=50&$count=true` | page, total count |
| `GET /odata/Products?$expand=Brand` | products with their brand |
| `GET /odata/Products({id})` | one product |
| `GET /odata/Products?$select=Name,Rate` | only these columns |

The permission of the set is checked on every call, tenants and soft delete filters apply as everywhere. `$top` is capped at 1000 (`Coworkee:OData:MaxTop`); invalid queries answer `400` with problem details.

## Facets

Mark the properties users should filter by:

```csharp
using Nextended.Core.Facets;

public sealed class Product : AuditedEntity, IMultiTenant
{
    [ProvideFacet(Label = "Brand", ValuePath = "BrandId", LabelPath = "Brand.Name", ValueType = typeof(Guid))]
    public Guid BrandId { get; set; }

    public Brand? Brand { get; set; }
}
```

`LabelPath` is a Dynamic LINQ expression. A navigation named like one of its keywords (`Parent`, `It`, `Root`, `New`, ...) needs the `it.` prefix: `LabelPath = "it.Parent.Name"`; without it the facet query fails. `coworkee entity add` always writes `it.`.

When the request asks for them with `Prefer: odata.include-annotations="cn.facets"`, the page carries the facet groups under `@cn.facets`: every option with label, count and the OData filter fragment to apply. The counts follow the current filter, the same way shop filters do. Enum options carry the member name as value and filter (`Status eq 'Shipped'`) and their `[Display]` name as label; `[Flags]` enums list each flag (`Options has 'Express'`). Number ranges and dates come with their smallest and largest value and a histogram of buckets. `[FacetBehavior]` next to `[ProvideFacet]`, or `services.ConfigureFacet<T>(...)`, sets the view and how values combine (*any of*, *all of*, single, with exclusion). With the `Coworkee-Facets` header a group counts without its own choice. [`CoworkeeDataTable`](../ui/data-table.md#facets) shows them in views that fit their values.

![Brand facet with counts](../assets/screenshots/products.png){ .shot }

## Row level rules

Some rows are not for everyone. An entity filter narrows lists, counts, facets and single reads alike:

```csharp
internal sealed class DocumentODataFilter(DocumentVisibility visibility) : IODataEntityFilter<Document>
{
    public Task<IQueryable<Document>> ApplyAsync(IQueryable<Document> query, CancellationToken cancellationToken) =>
        visibility.VisibleAsync(query, cancellationToken);
}

services.AddScoped<IODataEntityFilter<Document>, DocumentODataFilter>();
```

### Child rows follow their parent

Rows that belong to a parent (items of a talk, lines of an order) should be visible exactly when the parent is. Instead of repeating the parent's filter:

```csharp
services.AddODataChildFilter<TalkItem, Talk>(i => i.Talk);
```

An item is shown when the visible talks (every `IODataEntityFilter<Talk>`, tenant and soft delete, as `ODataEntityAccess.VisibleAsync<Talk>` returns them) contain its talk; the query is a subquery on the foreign key, no join. Items without a talk are hidden. It adds up with other filters of the child.

## Hiding properties

Internal fields stay out of the model, so they are neither returned nor filterable:

```csharp
services.AddODataEntity<Document>("Documents", DocumentPermissions.Documents.View, d => d.BlobKey);
```

### Properties behind a permission

Some fields are for some users only, salaries for HR for example. `HideUnless` keeps a property in the model but out of reach of users without the permission:

```csharp
services.AddODataEntity<Employee>("Employees", HrPermissions.Employees.View, o => o
    .Hide(e => e.PasswordResetToken)
    .HideUnless(e => e.Salary, HrPermissions.Salaries)
    .HideUnless(e => e.BirthDate, HrPermissions.PersonalData));
```

For a user without the permission:

- lists and single reads leave the property out (make it nullable in the client's DTO, so it reads as empty and not as `0`);
- `$filter`, `$orderby`, `$select`, `$expand`, `$apply`, `$compute` and facet filters naming it answer `403`, also through a navigation (`Manager/Salary`);
- its facet group is left out;
- exports leave the column out and refuse filters on it;
- reports neither list the field nor group, aggregate or filter by it;
- the global search does not match on it (a `Title` or `Subtitle` function of your own must not show it either);
- the assistant's `list_data_sets` and `query_data` neither list nor return nor filter it;
- the entity history leaves its changes out and a restore does not touch it;
- webhooks of an endpoint whose owner lacks the permission leave it out.

The check is per permission, not per row. The admin audit log (`Audit.View`) still shows every change.

## From the Blazor client

`IODataClient` builds the URL and reads values, count and facets:

```csharp
var page = await odata.QueryAsync<BrandDto>("Brands", new ODataQuery { OrderBy = "Name", Top = 1000, Count = false });
```

The BFF forwards `/odata` to the API with the user's token.

## Client entities

Mark an entity `[ClientEntity]` and Blazor clients keep a copy of its set and query it locally; see [Client entities](../ui/client-entities.md).

## Export and import

Every entity set can be exported as Excel, CSV or JSON with the list's `$filter`, `$search` and `$orderby`; `AddODataImport` makes a set importable through a wizard with column mapping, preview, upsert, dry run, background jobs and undo:

```http
GET /api/v1/data/Products/export?format=csv&$filter=Rate gt 10&$orderby=Name
```

```csharp
services.AddODataImport("Brands", (Guid? id, AddEditBrandRequest row) => new AddEditBrandCommand(id, row));
```

![Import result](../assets/screenshots/import.png){ .shot }

Everything about it is on [Import and export](import-export.md).
