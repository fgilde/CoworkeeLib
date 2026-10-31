---
title: "EF Core data layer and OData for .NET apps"
description: "Coworkee's EF Core data layer: PostgreSQL, SQL Server or SQLite, field-level audit, outbox, OData sets with facets, browser-side queries and Excel import."
---

# Data and queries

![Sales orders table with 100,000 rows and the measured median times of server and browser](../assets/screenshots/orders-compare-light.webp){ .shot loading=lazy }

That is the demo's sales order list: 100,000 rows, and the line under the facets is the table timing its own query on both sides. In this shot: server 141 ms, browser 58 ms. The browser wins because the rows are already there. (That shot is an older, lighter run; the measured table, 39-56 ms in the browser against 216-236 ms on the server, is in [Client entities](../ui/client-entities.md).)

Underneath sits plain EF Core. Handlers inject `CoworkeeDbContext` and call `db.Set<T>()`, with no repository layer in between (EF Core already is one).

## One DbContext, markers on the entities

The app derives a single context. Modules contribute their entities, the library adds identity, settings, audit and outbox tables.

```csharp
public sealed class MyAppDbContext(DbContextOptions<MyAppDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

context.Services.AddCoworkeeDbContext<MyAppDbContext>((provider, options) =>
    options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "myapp"));
```

What an entity gets depends on what it implements. `IMultiTenant` adds a query filter on the current tenant and fills `TenantId` on insert. `ISoftDelete` turns `Remove` into a flag that queries hide. `AuditedEntity` fills the created/modified columns, and `[Realtime(permission)]` pushes every committed change to SignalR clients holding that permission.

Changes to audited entities are also written field by field, with user, time, old and new value, in the same transaction. The admin UI shows them as a global *Audit log* and per record with `AuditTimeline`. `[Sensitive]` keeps the fact of a change but not the value.

Domain events (`Raise(...)` on an `AggregateRoot`) and integration events go through an outbox in that transaction too, so nothing is sent for a change that rolled back.

`Coworkee:Database:Provider` picks PostgreSQL (the default), SQL Server or SQLite. The provider-specific bits, such as outbox row locks or where Hangfire keeps its jobs, live behind small seams in the library. SQLite is for demos and one-machine setups: one writer at a time, background jobs in memory.

Tables of another database stay in their own plain `DbContext`: `AddCoworkeeExternalDbContext<TContext>()` lets OData sets, row filters, search, export, reports and client entities read its entities, and the unit of work saves it along with the app's context ([external databases](../fundamentals/persistence.md#external-databases)).

## OData sets with facets

```csharp
context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View);
```

That line gives the entity a set with `$filter`, `$orderby`, `$expand`, paging and counts. The permission is checked on every call, and tenant and soft delete filters apply as everywhere. Rows that only some users may see go through an `IODataEntityFilter<T>`, which narrows lists, counts and facets alike.

The options of a set go further: another key than `Id` (`o => o.Key(c => c.IsoCode)`), properties hidden or shown only with a permission (`Hide`, `HideUnless`), and a query limit of its own (`o => o.MaxNodeCount(1000)` instead of `Coworkee:OData:MaxNodeCount`). Child rows can follow the row filters of their parent (`AddODataChildFilter<TChild, TParent>`), and navigations follow the rules of their target. A second route with other names (`AddCoworkeeODataRoute("rt/odata", ...)`) serves the same sets under the same checks, and `IODataQueries.QueryAsync` runs a list query of a route in process, with count, facets and a table's facet choice. See [routes with other names](../web/odata.md#routes-with-other-names), [queries in code](../web/odata.md#queries-in-code) and [hiding properties](../web/odata.md#hiding-properties).

Properties marked `[ProvideFacet]` come back as facet groups: each option with label, count and the filter fragment that applies it. Counts follow the current filter, the way a shop sidebar does. Number and date facets carry a histogram. Most facets of a filtered set of up to `FacetRowLimit` rows (50,000) are counted in memory from one read, and `MaxFacetRows`, when set, leaves facets out for results larger than that ([how facets are counted](../web/odata.md#how-facets-are-counted)).

The same sets serve files. Exports write the current view as Excel, CSV or JSON, with headers of your choice (`columns=Number:Shipment no.`), in the names of another route (`route=`) and under a file name (`fileName=`). A progress stream reports the rows read and written and ends with a one-time download link; above `BackgroundExportRows` (10,000) it follows the export job, and a client that left gets the file by mail. Exports can also be mailed or scheduled. `AddODataImport` makes a set importable through a wizard: columns are matched to fields, values converted with the file's language, every row checked by the command's validators before anything is written, then imported (new, update by key or both) as a job with live progress, a file of the failed rows and an undo. See [Import and export](../web/import-export.md).

For conditions users assemble themselves, `QueryBuilder` produces nested *all / any* groups. `ODataQueryTranslator.ToFilter` turns the result into `$filter` text, `LinqQueryTranslator.ToExpression<T>` into an expression EF Core can run, and the tests make sure both return the same rows.

## Client entities: the browser answers

Put `[ClientEntity]` on an entity and the Blazor client keeps a copy of its OData set. While the copy downloads, queries go to the server. Once it is complete, filters, search, sorting and facet counts run locally, and changes arrive as deltas.

The copy is a small column store with per-column indexes, persisted to IndexedDB. For the 100,000 orders the first download takes about 14.5 s; after a page reload, queries are local again 0.3 s after the first rows show. With no filter, the table query measured 216-236 ms on the server and 39-56 ms in the browser.

Each table over such a set has a switch with *Compare and choose the faster*, which runs the current query three times on each side and keeps the winner. The copy only holds what the user could read through OData anyway. It is not encrypted on the device, though, so sets with sensitive data should stay on the server.

## Attributes and backups

`AddExtendedAttributes<T>` lets users hang key/value pairs (text, decimal, date or JSON) on any entity without a migration. `CoworkeeBackupModule` writes the whole database as one zip to the file storage and restores it in one transaction, refusing a backup from another migration.

**Status:** backups need PostgreSQL. MySQL and MariaDB are not offered, because Pomelo has no EF Core 10 release yet. Facets over `bool` columns fail on SQL Server and SQLite until the next Nextended release. The query builder cannot import OData text yet, and its rules cannot be reordered by drag and drop.

- [Persistence](../fundamentals/persistence.md): providers, base classes, migrations and seeding
- [OData and facets](../web/odata.md), [query builder](../ui/query-builder.md) and [client entities](../ui/client-entities.md) with the full measurements
- [Auditing](../modules/auditing.md), [extended attributes](../modules/extended-attributes.md) and [database backups](../modules/backups.md)
