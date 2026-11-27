---
title: "Layered data filters for ASP.NET Core and Blazor apps"
description: "Administrators define filters per entity set on four layers (system, tenant, group, role) that users never see: how they combine, details and linked sets, realtime and the admin UI."
---

# Layered data filters

Administrators define, per entity set, filters that are always applied and that users never see. They sit on four layers: **System**, **Tenant**, **Group** and **Role**. A user's own filtering, facets and counts work on what is left. Nothing tells a user that rows were removed: counts, facets, search and paging are computed on the filtered rows.

The filters apply wherever the library decides which rows a user may see: OData lists and `$count`, `$expand` targets, client entities (local copies, snapshots and deltas), global search, dynamic reports and exports. With no filter configured for a set there is no extra `Where`; finding that out costs one cached lookup of the organisation's filters (read from the database on a cold cache and inside a transaction).

## The four layers

| Layer | Applies to | Written with |
|---|---|---|
| System | every organisation | `DataFilters.ManageSystem` (system organisation only) |
| Tenant | the own organisation | `DataFilters.ManageTenant` |
| Group | members of a group (and its sub-groups) | `DataFilters.ManageGroups` |
| Role | users acting in a role | `DataFilters.ManageRoles` |

`DataFilters.Bypass` ("see every row") switches the Tenant, Group and Role layers off for its holder, and the System layer too when he belongs to the system organisation. The built-in Admin role holds every defined permission, so the administrators of every organisation hold Bypass: they see their organisation's data without its own filters, but System filters still apply to them; only the system organisation's administrators see the unfiltered data. Filters are stored as the `QueryGroup` of the [`QueryBuilder`](data.md) and translated to `$filter` on the server; saving validates them against the model of the set and refuses a filter that uses a property the saving administrator may not read (`HideUnless`).

## How the layers combine

System, Tenant and the user's active roles combine with **AND**. Active roles are the role the user acts in when the active role switch is on and a role is chosen, otherwise all roles of the user (direct and through groups), exactly as the permission checker sees them.

The group layer only **narrows the rows that come through a group**. For a set with an `IGroupRowSource<T>` (see below) a row is visible when

```text
NOT rowsOf(active groups)
OR rowsOf(unfiltered groups)
OR (rowsOf(Gi) AND fi ...)
```

that is: rows that reach the user through none of his active groups are left to the app's own scope, rows through a group without filters pass, and rows through a group with filters must pass that group's filter. One group's filter never cuts the rows of another group. Filters inherit downwards: rows that come through a group must pass the filters of the group **and** of all its ancestors (AND), so a child group never shows more than its parent allows.

The group layer is skipped for users with `Identity.Groups.ScopeAll`, for users the app's row source declares as seeing all, and for users without groups. Sets without an `IGroupRowSource<T>` use the rule "visible when the effective filter of at least one of the user's groups lets the row through"; one group without a filter lets everything through.

**Example.** A system filter on `CalculatedShipment` keeps the states Complete, Delivered and InProcess. The group *Alfa Laval* additionally keeps only shipments from Hamburg or Bremen. The role *Whatever* keeps only Completed. A member of Alfa Laval acting in that role sees completed shipments of Alfa Laval from Hamburg or Bremen, and filters further as before. A colleague in another group does not lose any row because of the Alfa Laval filter.

## Details and key reads

Every filter has an **Applies to details too** switch, on by default. On, a link or key access to a filtered-out record answers "not found". Off, the list hides the record but key access still returns it. Code that reads a single record by key runs inside `DataFilterScope.ByKey()`; the OData pipeline does this for key requests, and what a record expands is still a list and stays filtered.

```csharp
using (DataFilterScope.ByKey())
{
    var shipment = await visible.FirstOrDefaultAsync(s => s.Id == id, ct);
}
```

## Linked sets

Sets over the same data with different properties (`Shipment` and `CalculatedShipment`) are linked by their shared key on the admin page: *(set, property) → target set*. The filter is written once, on the set that has the properties. A linked set is narrowed by key: a `Shipment` row is visible only when the `CalculatedShipment` row with that key passes the target's filters (and its own link, so chains work). Both sets must live in the same `DbContext`, the property must have the type of the target's key, and a chain of links must not come back to a set in it; saving refuses each of these. A link whose target, property or key no longer exists fails closed.

## Invalid filters fail closed

A stored filter that no longer translates (a property was renamed or removed) becomes "no rows" for its layer and set, and the admin page shows it as invalid with the reason. It never throws for users and never lets rows through.

## Which rows come through a group: `IGroupRowSource<T>`

The library cannot know how an app relates rows to groups, so an app registers one source per set that is reached through groups:

```csharp
[RegisterAs(typeof(IGroupRowSource<Shipment>))]
internal sealed class ShipmentRowsOfGroups(IOrgCodes codes, IPermissionChecker permissions) : IGroupRowSource<Shipment>
{
    public async Task<Expression<Func<Shipment, bool>>> RowsOfAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken ct)
    {
        var org = await codes.OfGroupsAsync(groupIds, ct);
        return s => org.Contains(s.OrganisationCode);
    }

    public Task<bool> SeesAllAsync(CancellationToken ct) => permissions.IsGrantedAsync("Shipments.ViewAll", ct);
}
```

`IOrgCodes` and `OfGroupsAsync` stand for the app's own service that maps groups to the codes its rows carry. `RowsOfAsync` returns a predicate EF Core translates, for the rows of exactly these groups (not their sub-groups; the layer passes sub-groups it reaches with their own filters). Without a source a set uses the "any group's filter" rule. The group part negates the "reached through a group" predicate (`NOT rowsOf(active groups)`), so null group links matter, for users the group layer applies to and on sets with group filters. With the default EF null semantics a row with a null group link counts as "not through a group" and is left to the app's own scope; with `UseRelationalNulls(true)` `NOT(NULL)` stays NULL, so such rows drop out (fail closed).

## Role filters need `GetRoleIdsAsync`

Role filters apply to the user's active roles, read through `IPermissionChecker.GetRoleIdsAsync`. A custom permission checker that does not implement it returns no roles, so **role filters do not apply** for its users. Implement it when you replace the checker.

## Preview

The editor shows how many rows a draft filter lets through before it is saved. `POST /api/v1/data-filters/preview` takes the fields of a save and stores nothing:

```http
POST /api/v1/data-filters/preview
{ "entitySet": "Orders", "layer": "Tenant", "layerKey": null, "query": "{...}" }

200 { "passing": 4, "total": 10, "timedOut": false }
```

`total` counts the rows the editor sees through the app's own row filters with no data filter at all (for a system administrator every row); `passing` counts those that also pass the draft. Stored filters never count, so the number says what the draft alone would do. The request needs the permission of its layer, the read permission of the set and the same property checks as a save (400 for unknown or protected properties). Counting stops after `CoworkeeODataOptions.DataFilterPreviewTimeout` (default 5 seconds) and answers `{ "passing": null, "total": null, "timedOut": true }`. In code, `DataFilterScope.Off()` switches the data filters off for the current flow; the app's own row filters still apply.

## Overview

`GET /api/v1/data-filters/overview` tells per entity set how many filters the administrator's layer holds and whether the set is linked:

```http
GET /api/v1/data-filters/overview

200 { "layer": "Tenant", "tenantName": "Acme", "sets": [ { "entitySet": "Orders", "layerFilters": 1, "linked": false } ] }
```

The layer is `System` in the system tenant (needs `DataFilters.ManageSystem`) and `Tenant` elsewhere (needs `DataFilters.ManageTenant`). Links are reported in the system tenant only.

## Realtime

Every change reloads open tables and resynchronises local copies; the partition of a client copy includes a fingerprint of all filters of the user's organisation and the System ones (ids and versions, links included), of his active roles that carry a role filter and, when there are group filters, of his active groups, so a changed filter, a role switch or another group drops the copy and fetches the visible rows again. Without filters the partition gets nothing extra. Changes publish payload-free, actor-free signals: entities implement `IHasRealtimeSignals` to name the topics, and the event type is `RealtimeEventTypes.Signal`. A signal carries an empty payload (no entity name, id or filter content) and no actor. Tenant, group and role filters signal on `tenant:data-filters` (their own organisation only), System filters and links on `global:data-filters`. "Signal" is a reserved event name: an app must not use it for events of its own.

## Admin UI

- **Data types** lists every entity set with its client entity settings. A search field narrows the list by set or type name, and two chips keep only sets with a local copy or only sets with data filters. Each row ends with the same actions: a pencil (local copy), reset, and a filter icon that opens the row's filters below it. The filter icon is coloured and carries a badge (filters of your layer plus the link) when the set is filtered, so you see at a glance which sets are. The panel is titled "Filter for all tenants" in the system tenant (the System layer and the link editor) and "Filter for tenant Acme" in every other tenant (that tenant's layer only). Group and role filters are edited on the group and role pages. The counts follow changes live.
- The **Data filters** tab on the group page and the role page edits the filters of that group or role.
- The filter editor is the `QueryBuilder`: drag rules and groups into other groups or positions (or use the node menu: up, down, into a group, out of a group), switch a group between AND and OR, wrap ticked conditions into a new group, unwrap a group. Unwrap is offered only when the meaning stays the same: the group's logic equals its parent's, or it has at most one child, and it is not negated.
- Under the editor's title a line names whom the filter does not bind: holders of **See all rows** (the tenant's admins; for System filters, those of the system tenant). Under the builder a live preview line reads, for example, "4 of 1,200 rows you can see pass this filter". It is recomputed 400 ms after the last edit (and once when a stored filter opens) through the preview endpoint, shows "Preview not available: counting took too long." when the count timed out and no line while the draft is invalid.

Reading and writing needs the permission of the layer; the page and the navigation entry show for anyone with one of the `DataFilters.Manage*` permissions.

## Moving to a version with data filters

The tables `cw.DataFilters` and `cw.DataFilterLinks` must exist before the app starts: every entity-set read looks them up on a cold cache. Add and apply an app migration (`coworkee migrations add AddDataFilters`) first; the library ships no migration of its own. The query model moved to `Coworkee.Contracts.Query`; see the [changelog](../reference/changelog.md).
