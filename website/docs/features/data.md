---
title: "EF Core data layer and OData for .NET apps"
description: "Coworkee's EF Core data layer: PostgreSQL, SQL Server or SQLite, field-level audit, outbox, OData sets with facets, browser-side queries and Excel import."
---

# EF Core data layer with OData, audit trail and browser-side queries

Most business apps spend a surprising share of their code on plumbing around the database: list endpoints with paging and filters, an audit table someone forgot to fill, soft delete flags that leak into half the queries, an export button that ignores the user's filter. Coworkee does this once, in the library, so the app's code is the domain model and the commands that change it.

The data layer is plain EF Core. There are no repositories and no custom query language: handlers inject `CoworkeeDbContext` and call `db.Set<T>()`. What the library adds sits around that context: conventions for tenants, soft delete and auditing, an outbox, and a way to expose every entity as a filterable OData set.

![Sales orders table with 100,000 rows and the measured median times of server and browser](../assets/screenshots/orders-compare-light.webp){ .shot loading=lazy }

## One DbContext for the whole modular monolith

The app derives one `DbContext` from `CoworkeeDbContext`. Modules contribute their entities; the library contributes identity, settings, audit and outbox tables.

```csharp
public sealed class MyAppDbContext(DbContextOptions<MyAppDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

context.Services.AddCoworkeeDbContext<MyAppDbContext>((provider, options) =>
    options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "myapp"));
```

Behaviour comes from base classes and markers on the entity:

| Marker | Effect |
|---|---|
| `AuditedEntity` | `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy` filled on save |
| `IMultiTenant` | query filter on the current tenant, `TenantId` set on insert |
| `ISoftDelete` | `Remove` marks the row deleted, queries hide it |
| `IHasConcurrencyToken` | optimistic concurrency with a `Version` column |
| `IVersioned` | a snapshot per revision for the version history view |
| `[Realtime(permission)]` | inserts, updates and deletes are pushed to SignalR clients with that permission |

Domain events raised with `Raise(...)` on an `AggregateRoot` and integration events published through `IEventBus` go into the outbox of the same transaction. `AddCoworkeeOutboxProcessing<TContext>()` sends them after the save and retries failures, so an event never goes out for a change that was rolled back.

## PostgreSQL, SQL Server or SQLite

`Coworkee:Database:Provider` picks one of three databases. PostgreSQL is the default and the choice for production. SQL Server fits organisations that already run it or Azure SQL. SQLite keeps everything in one file and is meant for demos, prototypes and single-machine installations. The provider-specific parts (concurrency tokens, JSON columns, outbox row locking, distributed locks, background job storage) sit behind small seams in the library; the app does not change.

```bash
coworkee new Shop --database sqlServer
```

Migrations belong to the provider the app was created with. Database backups need PostgreSQL.

## Field-level audit trail

Every change to an audited entity is written field by field: who, when, old value, new value. It is stored in the same transaction as the change itself. The admin UI has a global *Audit log* page and an `AuditTimeline` component for the history of a single entity. `[NotAudited]` leaves a type or property out, `[Sensitive]` records that a property changed without storing its value.

![Audit log with entity, action and user filters and the changed fields per row](../assets/screenshots/audit-log-dark.webp){ .shot loading=lazy }

## OData sets with facets for every entity

One line turns an entity into an OData set with its permission:

```csharp
context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View);
```

Tables, exports and external tools read through it: `$filter`, `$orderby`, `$select`, `$expand`, paging and counts. The permission is checked on every call, and tenant and soft delete filters apply as everywhere else. Row-level rules go into an `IODataEntityFilter<T>`, which narrows lists, counts, facets and single reads alike. Internal fields can be left out of the model completely.

Properties marked with `[ProvideFacet]` come back as facet groups on request: every option with label, count and the OData filter to apply. Counts follow the current filter the way shop filters do. Enums, `[Flags]` enums, number ranges with histograms and date ranges are covered; `[FacetBehavior]` sets how values combine and which view the table shows.

## A query builder that speaks OData and LINQ

`QueryBuilder` lets users build nested *all / any* groups of conditions with operators that fit each field type. The query is a plain model: `ODataQueryTranslator.ToFilter` turns it into an OData `$filter`, `LinqQueryTranslator.ToExpression<T>` into a LINQ expression that EF Core translates, and `QueryJson` saves it. Both translators are tested against each other and return the same rows.

## Client entities: 100,000 rows answered in the browser

Mark an entity `[ClientEntity]` and the Blazor client keeps a copy of its OData set in the browser. Until the copy is complete, queries go to the server; after that, filtering, search, sorting, grouping, paging and facet counts run locally, and changes arrive as small deltas. The copy is a small column store with indexes, kept in IndexedDB, so a reload is local again within a few hundred milliseconds.

The template shows it with 100,000 sales orders. Every table over such a set gets a switch with *Compare and choose the faster*: it runs the table's current query three times on each side, shows the median times and picks the faster one. With no filter, the measured median was 216-236 ms on the server and 39-56 ms in the browser. The copy contains only rows the user may read through OData, so permissions, tenants and row filters apply to it as well.

## Extended attributes, Excel import and export

`AddExtendedAttributes<T>` lets users attach key/value pairs (text, decimal, date or JSON) to an entity without a schema change, edited with `ExtendedAttributesEditor` or a dialog from a table row.

Every OData set exports to Excel with the same filter and sort as the list, capped by `Coworkee:OData:MaxExportRows` (100,000 by default). `AddODataImport` turns each row of an uploaded sheet into a command, so validation and permissions run per row, and failed rows come back with their row number.

## Database backups

With PostgreSQL, `CoworkeeBackupModule` saves the whole database as one zip in the file storage and restores it in one transaction. A restore refuses a backup made at another migration. Backups hold every organisation, so only users of the system organisation with `Backups.Manage` see the page.

## Where to go next

- [Persistence](../fundamentals/persistence.md): DbContext, database providers, base classes, migrations and seeding
- [OData and facets](../web/odata.md): entity sets, row-level rules, Excel export and import
- [Query builder](../ui/query-builder.md): fields, operators, translators and saved queries
- [Client entities](../ui/client-entities.md): settings, the switch, performance measurements and limits
- [Auditing](../modules/auditing.md): audit log, timelines and version history
- [Extended attributes](../modules/extended-attributes.md): endpoints and client components
- [Database backups](../modules/backups.md): what a backup contains and the database rights a restore needs
