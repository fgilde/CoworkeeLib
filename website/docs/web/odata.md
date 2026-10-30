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

OData's other query limits are options of `Coworkee:OData` too. They apply wherever the library reads a caller's query: the entity sets on every route, `IODataQueries`, exports, `query_data` (MCP and the assistant), facet filters (`FacetResponse.FilterAsync`), row filters of code (`ODataEntityAccess.FilteredAsync`, e.g. mail recipients) and reports, computed sources included; own code checks options it parses with `ODataLimits.Check(options, services)`. The limits: `MaxNodeCount` (nodes of `$filter` and `$orderby`, default 100, which a search ORing `contains` over many fields soon exceeds), `MaxAnyAllExpressionDepth` (nested `any`/`all`, default 1), `MaxExpansionDepth` (nested `$expand`, default 2) and `MaxOrderByNodeCount` (properties in `$orderby`, default 5). Values below 1 (below 0 for `MaxExpansionDepth`) stop the start. Raise them only as far as a client needs: larger limits let every user send more expensive queries.

The service document (`GET /odata`) and `GET /odata/$metadata` list every set and property name of the model, so they need a sign-in on every route (`401` without one), whatever the user's permissions. `Coworkee:OData:AnonymousMetadata` (`CoworkeeODataOptions.AnonymousMetadata`, default `false`) opens both again for clients that read the model anonymously; the sets keep their checks.

### Keys other than `Id`

A set keyed by another property, of any primitive type, names it with `Key`:

```csharp
services.AddODataEntity<Country>("Countries", MasterDataPermissions.View, o => o.Key(c => c.IsoAlpha2Code));
```

`GET /odata/Countries('DE')` reads one row (`/odata/Ranks(2)` for a number key). Lists, `$count`, facets, the global search (the hit's id is the key), exports, reports and `query_data` work as for any set. Features that address rows by a Guid `Id` refuse an entity without one when they are registered: record attachments, social (comments, tags, ratings, reactions), workflows, entity history and webhook entity events. Client entities leave such sets on the server. Imports that update or upsert (`AddODataImport` with an id) find rows by the set's key, which must be a Guid; for other keys register the import without an id (create only), else the registration throws. `AddExtendedAttributes` refuses an entity without a Guid `Id`. An entity that keeps a Guid `Id` next to another OData key still has them all.

Names in the URI match regardless of case, so a camelCase client may send `$filter=name eq 'x'`, `$orderby=price` or `$expand=brand`; `Coworkee:OData:CaseInsensitive` (default `true`) turns that off. Responses on `/odata` keep the names of the C# properties (`Name`); a [route with other names](#routes-with-other-names) answers in camelCase. Enum properties compare with names (`Status eq 'Shipped'`) and with the member's number (`Status ne 16`); `in` needs names. Query options need their `$`: `filter=` is not read. A `400` answer explains OData's own errors in `detail`; other failures of a query (for example EF cannot translate it) carry no detail and are logged, because their messages can quote row filters.

### Routes with other names

A client that expects other property names (camelCase JSON, names of an older API) gets a second route over the same sets:

```csharp
services.AddCoworkeeODataRoute("rt/odata", route => route
    .CamelCase()                                                       // name, isoAlpha2Code, eta
    .Alias<IHubAudited>(e => e.DateCreatedUtc, "creationTime")         // a base class or interface: every type with it
    .Alias<IHubAudited>(e => e.DateUpdatedUtc, "lastModificationTime")
    .FacetsOnWildcardPrefer());                                        // the default; false builds facets for "cn.facets" only
```

`/rt/odata/Shipments?$filter=creationTime gt 2026-01-01T00:00:00Z&$orderby=creationTime&$select=name,creationTime&$expand=customer` works and answers with these names; the C# name of an aliased property is unknown on that route. The route has its own model built from the same registrations and runs exactly the steps of `/odata`: the set's permission, row filters, `Hide` and `HideUnless`, computed properties and their permissions, the navigation scope, the query checks, `$search` and facets. The rules follow the C# member whatever it is called, so an aliased `HideUnless` property is refused and left out like under its own name. Facets on the route name their field, value path and option filters in the route's names, so their filters work as `$filter` there; the facet context header is read in them too. `ODataRoutes.Find("rt/odata").AliasOf(type, "DateCreatedUtc")` gives the alias for own JSON that should match the route. Two members of one type that end up with the same name (case does not count) fail at startup.

`/odata` stays as it is: the Blazor client, exports, reports, `query_data` and client entities use it. Exports (`/api/v1/data/{set}/export`), `query_data`, reports and the global search take C# names only, whatever route the client reads: an alias or a lower camel case name that differs from the C# name answers `400` there (an unknown property), so a client of a renaming route translates names back before it calls them.

### Queries in code

`IODataQueries` runs a list query of a route in process, for the current user, through the same steps as the HTTP route; REST endpoints that answer from an entity set use it:

```csharp
var page = await odata.QueryAsync<Shipment>("rt/odata", "Shipments", "$filter=state ne 16&$orderby=etd&$top=20&$count=true", facets: true, ct);
if (!page.IsSuccess) return page.Error.ToHttpResult();
var (rows, shaped, count, facets) = page.Value;
```

`Rows` are the entities when the query does not shape them; what the route would not write (hidden or protected properties, members outside the model) is cleared to its default, through the setter, the field EF reads the member from, or the field of an auto-property. With `$select`, `$expand`, `$apply` or an `[AutoExpand]` navigation the rows are in `Shaped` instead: dictionaries by the route's names, expanded navigations nested, members the user may not read left out. A hidden or protected member, or one EF maps, that can be cleared none of these ways (a getter over a field EF does not know) puts the whole page into `Shaped` too: no entity is handed out with it. `Count` is set with `$count=true`, `Facets` when asked for, in the route's names; a page size of the model (`[Page(PageSize = 50)]`) cuts the rows as on the route. A query the route refuses comes back as a `Forbidden` or `Validation` error, a user who is not signed in as `Unauthorized`; a query the database cannot run is logged and comes back as a `Validation` error without its message, which could quote row filters.

### The model from EF

By convention the OData model takes every public property with a public setter and a key named `Id` or `{Type}Id`. With `Coworkee:OData:ModelFromEntityFramework` set to `true` (`CoworkeeODataOptions.ModelFromEntityFramework`), keys and properties come from the EF model of each set's DbContext, external ones included:

- every entity type the sets reach through EF navigations is keyed by its EF primary key, so a `PackLineId` column next to an inherited `Id` is a plain property and `$expand=PackLines` works (by convention both are key candidates, the type gets none and becomes a complex type);
- properties EF maps without a public setter (`public int State { get; private set; }`, a computed column) are in the model and can be filtered, sorted and selected.

Such properties join only the types of registered sets, where `Hide` and `HideUnless` reach them; a type reached only through navigations keeps its conventional members (and gets its EF key). The app logs a warning at startup for every property it adds. It is off by default because it adds properties that were not in the model before: `Hide` or `HideUnless` what users must not see before you turn it on. Properties a set hides stay out.

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

When the request asks for them with `Prefer: odata.include-annotations="cn.facets"` (or `"*"`, unless the route turns that off), the page carries the facet groups under `@cn.facets`: every option with label, count and the OData filter fragment to apply. The counts follow the current filter, the same way shop filters do. Enum options carry the member name as value and filter (`Status eq 'Shipped'`) and their `[Display]` name as label; `[Flags]` enums list each flag (`Options has 'Express'`). Number ranges and dates come with their smallest and largest value and a histogram of buckets. `[FacetBehavior]` next to `[ProvideFacet]`, or `services.ConfigureFacet<T>(...)`, sets the view and how values combine (*any of*, *all of*, single, with exclusion). With the `Coworkee-Facets` header a group counts without its own choice. [`CoworkeeDataTable`](../ui/data-table.md#facets) shows them in views that fit their values.

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

### Navigations follow their target

A query reaches other rows through navigations: `$expand` (also `[AutoExpand]` navigations, which expand without it), `$filter=Invoices/any(...)`, `$orderby=Customer/Name`, nested `$count`, `$compute`, `/$count`, facets and their labels, export and `query_data` filters, global search fields, report fields. They show exactly what the target's own set lists for the user:

- a collection only has the rows the target's `IODataEntityFilter<T>`s let through, and `any`, `all` and `$count` see only those;
- a single navigation (`Customer`) is `null` when its row is hidden, at every level (`Invoice/Shipment/Customer`), and a collection under a hidden reference is empty;
- `$expand` into a set the user lacks the permission of answers `403` naming the navigation; in a `$filter`, and behind an `[AutoExpand]` navigation, that set has no rows;
- protected properties (`HideUnless`), hidden members of a shared base and computed properties with a `Permission` of the target stay hidden, also through the navigation: naming them answers `403`, expanded rows leave them out;
- a type with row filters but no set of its own gets its filters too;
- a navigation to a base type (`Invoice.Shipment` of type `ShipmentBase`) applies the filters and permissions of every derived set to its rows;
- facet counts come from the narrowed rows, and a facet label read through a navigation (`[ProvideFacet(LabelPath = "it.Customer.Name")]`) shows the key instead of the name when the user may not see that row;
- a facet whose value is read through a navigation into filtered rows (`ValuePath = "Buyer.OrganizationId"`, `"it.Customer.Region"`) groups and counts only the targets the user may see. A row whose target is hidden has no value there, as `$expand` shows `null`, and is left out of that group, like a row without a target; neither its key nor its label appears. Choices in the group combine as usual (*any of* keeps its alternatives).

The filters are applied inside the query, so they must be plain `Where` calls on the query they get (closures with ids, `EF.Parameter`, subqueries and `AddODataChildFilter` are fine). A filter that does more (`OrderBy`, `Take`, `Join`, a new query), or fails on the empty in-memory query it is read from (one that asks the database first), cannot be applied to a navigation: `$expand` into its type answers `400`, and in a `$filter` the type has no rows. Other queries are not affected: only the targets a query actually reaches are checked, once per request. The set's own list still uses the filter as it is. EF query filters of the DbContext (tenant, soft delete) apply to navigations as always.

The checks run on the query OData binds, not on its text: every property and navigation read in `$filter`, `$orderby`, `$compute`, `$apply`, `$select` and `$expand` (nested options too) is checked against the denied members of the type it is read from, its bases and, read through a base type, every derived set. So `?+$filter=`, `%20$expand=` and `Featured/ Size` are checked like any other spelling, and a literal such as `'a/Note'` is no property. Members are checked whatever node reads them, entity-set aggregates (`aggregate(Employees(Salary with max as M))`) and options nested in `$expand` included, and a query the check cannot parse answers `400` instead of running. `$search` looks in the fields an `ISearchable` entity names, else in the fields of the set's [global search](global-search.md) (`AddGlobalSearch<T>(s => s.Fields(...))`), else in every string property. It matches only what the user may read (no `Hide`, no `HideUnless` without the permission), in lists and exports; a search field an `ISearchable` entity names (`x => x.FirstName + " " + x.LastName`, `x => x.Note.ToLower()`) is left out when any member it reads is hidden or denied, or reached through a navigation whose target has row filters or a permission the user lacks. Parameter aliases (`$filter=@a gt 5&@a=Size`) are refused with `400`, and so is `$apply` through a navigation into rows with row filters or permissions (a hidden target would make a group of empty values); `$apply` over the set's own properties, or through navigations without such rules, works. A facet is left out when its value or label path reads a member denied to the user, wherever it is: a property of the row other than the one carrying `[ProvideFacet]` (`ValuePath = "ManagerId"`), or one behind a navigation (`LabelPath = "it.Customer.Rating"`); a label path that is an expression rather than a path cannot be checked and leaves the facet out too. A facet whose value is read through a navigation (`ValuePath = "it.Customer.Name"`) is also left out when the user may not read the target's set (no permission) or when the target's row filters do more than `Where` (they cannot apply inside the facet query), also through an owned type; targets with plain row filters count their visible rows only (see above), keys and number or date ranges included (`Customer/Id`, the smallest and largest value of the visible targets). Facet groups come from `[ProvideFacet]` on public properties of the entity; a group whose paths were not checked that way is left out instead of failing the request. A facet through a collection (`it.Lines.Id`) is refused when the model is built. `$expand=*($levels=2)` into a set the user may not read comes back empty instead of `403`, like an `[AutoExpand]` navigation.

Not covered: own code that composes navigations on `ODataEntityAccess.VisibleAsync` without the scope, and the `Title`, `Subtitle`, `Url` and `Thumbnail` functions of the global search (they see the row's own columns only). `ODataNavigationScope.For(services, typeof(T))` gives the rules to own queries: `await scope.ApplyAsync(query, ct)` narrows every navigation in it, outside the rows `VisibleAsync` returned.

## Computed properties

A value computed from other columns, or from other tables, is an `ODataComputedProperty` of the set. The database evaluates it, so `$filter`, `$orderby` and `$select` work on it; it comes back only when `$select` names it:

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

An expression over the row's own columns works on every set, also on one of an [external database](../fundamentals/persistence.md#external-databases). A subquery on another table uses `ComputedQueryRoot.Of<T>()` as its source; that needs the app's own DbContext. `Permission` hides the property from users without it, `Exported` makes it an export column, `Facet` gives it a facet group. A getter-only C# property (`public string Display => ...`) is no column; the OData model leaves it out, so expose such a value this way.

## Hiding properties

Internal fields stay out of the model, so they are neither returned nor filterable:

```csharp
services.AddODataEntity<Document>("Documents", DocumentPermissions.Documents.View, d => d.BlobKey);
```

`Hide` and `HideUnless` also take members a base class declares (`o => o.Hide(s => s.Tags)` with `Tags` on `ShipmentBase`). Usually the EDM gives every set its own copy of the base's members, and they leave only this set. When a navigation points to the base class (`Invoice.Shipment` of type `ShipmentBase`), the EDM has the base as a type of its own that every derived set shares, and a hidden member cannot leave it for one set alone. It then stays in the model, and on the hiding set it is refused for everyone like a `HideUnless` property nobody holds the permission of: not serialized, `$filter`, `$orderby`, `$select` and `$expand` naming it answer `403`, exports, reports and search leave it out. Other sets of the base keep it. The app logs a warning at startup. Rows reached through a navigation to the base leave out the hidden properties and navigations of their own set (`Stickers?$expand=Owner($expand=Stickers)` has no stickers on owners whose set hides them).

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
