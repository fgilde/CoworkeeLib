# Group tree and group scope (organisation units)

Status: design, 2026-10-26. Driver: CoShipee (`C:\dev\privat\github\CoShipee`, rebuild of the CargonerdsApp on Coworkee), inventory in its `docs/inventory.md` (lib gap L1).

## Why

B2B apps need organisation units: customers, subsidiaries, branches as a tree, users as members of any number of units, and data that a user sees **because** of the units they belong to. CoShipee needs it on day one (every shipment, booking, quotation and tag is scoped by units), and other B2B apps on Coworkee will need the same. So it goes into the library, not into the app.

Coworkee's `UserGroup` already has members, a tenant, an admin page, an OData set, search, realtime topics and workflow/mail/resource-permission integration. It lacks the tree and the "which units am I working in right now" scope. This design adds both to groups instead of creating a second, parallel concept.

## What the owner decided

- Organisation units are **groups with a parent**. One concept in the library. (Owner: "sieht so aus, als wäre UserGroup schon ganz gut das was wir wollen".)
- **Roles and permissions are independent of units.** A user can hold any role and belong to one unit, or to many. Units never grant permissions by themselves. (Groups can still carry roles, as today; apps that use groups as units simply do not assign roles to them.)
- A user is a member of **any number** of units.
- **Scope selection**: a picker in the app bar lets the user choose the units they are working in.
  - **Nothing selected** = everything of every unit the user is a member of (and their sub-units).
  - **Some units selected** = only those units (and their sub-units).
  This differs from the CargonerdsApp, where an empty selection falls back to the first membership.
- The **unit management page** of the CargonerdsApp's Blazor admin is rebuilt in Coworkee (tree, CRUD, move, members, import/export).
- Built properly ("mach aber ordentlich"): tests, docs EN + DE, changelog, template migration.

## Goals

1. Groups form a tree per tenant (`ParentId`, materialised `Code` path).
2. Create under a parent, rename, move (with its subtree), delete (with its subtree).
3. Members: add, remove, move all members to another group; existing set-all stays.
4. A server-side **group scope** service apps use in their row filters: memberships, accessible groups (memberships + descendants), the user's selection, and the resulting active groups.
5. The selection is stored per user on the server (survives reloads and devices, the realtime React client of CoShipee pushes it once and expects the server to apply it to every request).
6. Blazor: tree management page replacing the flat groups table, an app-bar picker component, extension point for app-specific detail tabs (CoShipee adds org codes, owners, notification recipients).
7. Import/export of groups through Coworkee's existing data transfer (OData import/export), parent addressed by path.

## Non-goals

- Units granting permissions (owner: independent). No role inheritance down the tree.
- Group-scoped row filters inside the library for app entities: the library gives the scope, apps write `IODataEntityFilter<T>` with it (that mechanism exists and covers OData, search, export, client entities).
- The CargonerdsApp specifics: the "Default Organization Unit" whose records are private per user, the unit ↔ hub organisation code mapping, unit owners ("account owners"), the Rockline rule. Those stay in CoShipee.
- A per-group "is a unit" flag. YAGNI: an app that uses groups as units treats all groups as units; HR-style apps simply do not place the picker.
- AirWorks / TypeScript admin apps: they keep showing groups flat (follow-up).
- Realtime topics per group. `[Realtime]` still broadcasts `type:X` to everyone with the view permission and `entity:X:{id}` subscriptions check only the permission. With units as customers in one tenant, other customers' clients learn record ids and changed property names and reload. Follow-up design (group topics + an authorizer on `IGroupScope`); the docs warn about it now. CoShipee's hub data is not affected (no Coworkee interceptors on the hub context).
- Tree-aware membership in existing group features: workflow approvers (`ApproverDirectory`), mail to groups (`MailRecipientDirectory`), resource permissions (`ResourceAccess`) and group roles (`PermissionChecker`) keep using **direct** membership.

## Anti-goals (would be a failure even if it works)

- Breaking existing apps: flat groups must keep working unchanged (they become roots), endpoints keep their shapes (only additive fields).
- A selection that widens access: the active set is always a subset of what the user's memberships reach.
- Leaking across tenants through codes, parents or the selection.

## Design

### Model (`Coworkee.Identity`, table `cw.Groups`)

- `UserGroup.ParentId` (`Guid?`, FK to `UserGroup`, `DeleteBehavior.Restrict`; subtree deletes are done by the handler).
- `UserGroup.Code` (`string`, max 95): materialised path of 5-digit segments, `00001.00003.00002`, unique per tenant (`(TenantId, Code)`). Roots `00001`, `00002`, ...; a child's code = parent code + `.` + next free segment among its siblings. Descendants of X = `Code.StartsWith(X.Code + ".")`. Max depth 16 (95 chars), validated.
- Name uniqueness moves from `(TenantId, Name)` to `(TenantId, ParentId, Name)` with nulls not distinct (two roots may not share a name; siblings may not; cousins may).
- New table `cw.GroupSelections (UserId, GroupId)`, key both, cascade from user and group. Holds the user's picked groups.
- Migration: existing rows get `ParentId = null` and codes `00001..n` ordered by name. Done in the migration with SQL per provider (Postgres, SQL Server, SQLite), or by a one-time startup fix-up for rows with an empty code if SQL per provider gets ugly (builder's call; the result must be the same).
- `Code` is maintained only by the handlers; DTOs expose it read-only.

### Commands and queries (`Coworkee.Identity/Groups`)

Existing requests keep their shape; additions:

| Request | Permission | Notes |
|---|---|---|
| `CreateGroup(GroupRequest)` | `Groups.Manage` | `GroupRequest` gets optional `ParentId`. Parent must exist in the tenant. |
| `UpdateGroup` | `Groups.Manage` | unchanged (name, description). |
| `MoveGroup(Id, ParentId?)` | `Groups.Manage` | new parent null = root. Rejects moving under itself or a descendant (validation error), rejects depth > 16, rejects name clash under the new parent. Recomputes the codes of the group and its whole subtree in one unit of work. |
| `DeleteGroup(Id)` | `Groups.Manage` | deletes the group **and its subtree** (CargonerdsApp behaviour), with each group's permission grants and resource permissions. The UI confirms with the number of sub-groups and members. |
| `AddGroupMembers(Id, UserIds)` / `RemoveGroupMembers(Id, UserIds)` | `Groups.ManageMembers` | users must be in the tenant. |
| `MoveGroupMembers(FromId, ToId)` | `Groups.ManageMembers` | moves all members (the "move all users" button). |
| `SetGroupMembers` | `Groups.ManageMembers` | existing, permission relaxed from Manage to ManageMembers (Manage implies ManageMembers). |
| `GetGroupChildren(ParentId?)` | `Groups.View` | `GroupNodeDto(Id, ParentId, Code, Name, Description, ChildCount, MemberCount)` ordered by name. Lazy tree. |
| `SearchGroupTree(Search)` | `Groups.View` | matching groups plus their ancestors as `GroupNodeDto`s, so the page can show a filtered tree. Capped (e.g. 200 matches). |
| `GetMyGroups` | signed in | the user's accessible groups as nodes + `Selected` flag + `IsMember` flag: what the picker shows. |
| `SelectGroups(GroupIds)` | signed in | stores the selection; every id must be accessible to the user (else validation error, nothing stored). Empty list = reset to "all". |

Permissions: `Identity.Groups.View`, `Identity.Groups.Manage` (exist), new `Identity.Groups.ManageMembers` (implied by Manage), new `Identity.Groups.ScopeAll`. Texts in all 23 languages.

Endpoints (under the existing identity API group): `GET /groups/children?parentId=`, `GET /groups/tree?search=`, `PUT /groups/{id}/parent`, `POST /groups/{id}/members/add`, `POST /groups/{id}/members/remove`, `POST /groups/{id}/members/move` (body: target id), `GET /groups/mine`, `PUT /groups/mine/selection`. Exact paths are the builder's call as long as they follow the module's style. `GroupDto` gets `ParentId` and `Code` (additive).

### Group scope (the API apps use)

Interface in `Coworkee.Application` (so app modules need no Identity reference), implementation in `Coworkee.Identity`, scoped:

```csharp
public interface IGroupScope
{
    Task<IReadOnlyList<Guid>> MembershipsAsync(CancellationToken ct);   // direct memberships
    Task<IReadOnlyList<Guid>> AccessibleAsync(CancellationToken ct);    // memberships + all descendants; every group of the tenant with Groups.ScopeAll
    Task<IReadOnlyList<Guid>> SelectedAsync(CancellationToken ct);      // effective selection (see "Where the selection applies") ∩ accessible
    Task<IReadOnlyList<Guid>> ActiveAsync(CancellationToken ct);        // selection + descendants, or Accessible when nothing is selected or the selection does not apply
    Task<IReadOnlyList<GroupNode>> NodesAsync(IEnumerable<Guid> ids, CancellationToken ct); // Id, ParentId, Code, Name of any groups of the tenant
    Task<IReadOnlyList<GroupNode>> AncestorsAsync(Guid id, CancellationToken ct);           // root first, without the group itself
}

public sealed record GroupNode(Guid Id, Guid? ParentId, string Code, string Name);
```

`GroupNode` and `IGroupScope` live in `Coworkee.Application` (app modules do not reference Identity). They are public API from the first release, so the tree accessors are in from the start (CoShipee needs "root unit of the active unit" for tags and parent fallbacks).

- Computed once per request (scope lifetime) with at most two queries; no cross-request cache in the first version (`ponytail:` note, add the permission cache's invalidation pattern if it shows up in profiles).
- No user (anonymous, system jobs without a user) → all lists empty. Apps decide about bypasses (e.g. a `ViewAll` permission) in their own filters.
- Group, membership or selection changes take effect on the next request.
- `ScopeAll` holders: see "Scope over the whole tenant".
- Usage (documented): `internal sealed class ShipmentFilter(IGroupScope scope) : IODataEntityFilter<Shipment>` → `var ids = await scope.ActiveAsync(ct); return query.Where(s => ids.Contains(s.UnitId));`.

### Where the selection applies (narrowing vs. access)

The selection decides **what lists show, not what a user may open** (as in the CargonerdsApp). App filters are written once against `ActiveAsync`; the library widens it where access, not listing, is meant:

- `Coworkee.Application` gets an ambient switch `GroupScope.Unnarrowed()` (AsyncLocal, `using` scope). Inside it `ActiveAsync` returns `AccessibleAsync`.
- The library wraps in it: OData get-by-key (`EntityODataController` single-entity reads), `ODataEntityAccess.VisibleIdsAsync` (used by attachment, social, history and child guards), import matching (`ImportPlanner`/`ImportService`), webhooks/workflow entity reads that go through the visible set. The builder lists every `VisibleAsync`/`VisibleIdsAsync` caller and classifies it as listing (narrowed) or access (unnarrowed); listing = OData collections, `$count`, facets, global search, export, client entities, reports.
- The selection applies only to interactive sign-ins (browser through the BFF, OIDC user tokens of interactive clients). It is ignored for personal API keys (MCP signs in with one), service clients and background jobs: they see `AccessibleAsync`. The assistant runs inside the user's browser session and follows the selection like the page it sits on. The builder picks the detection (authentication scheme / `ICurrentUser.ClientId` / no HTTP context).
- Exports started from the UI capture the active selection at request time and run the job under `GroupScope.Selection(ids)` (AsyncLocal override), so the file matches the list the user saw.
- Default without any selection: `ActiveAsync == AccessibleAsync`.

### Scope over the whole tenant

New permission `Identity.Groups.ScopeAll` ("Work in any group"): `AccessibleAsync` = every group of the tenant. For operators who belong to no unit and must narrow to one customer. The picker then loads lazily (`GetGroupChildren`/`SearchGroupTree`) instead of the full accessible list. Bypasses for rows without any unit stay the app's business.

### Client entities

The browser mirror holds the rows the server shows, so it must not survive a change of the active groups. The client-entity manifest partition gets an extension point: `IClientEntityPartitionPart` (enumerable, each returns a string or null); Identity contributes a hash of the active group ids when the user has a selection. `SelectGroups` success → client calls `ClientEntities.ClearAsync()` (or reloads the manifest) so the mirror resyncs under the new partition.

### Realtime

- Group create/update/move/delete and member changes publish on the existing `type:UserGroup` / entity topics (already wired through `IdentityRealtimeTopics`).
- `SelectGroups` publishes to the user's own topic (`user:{id}` style, whatever the realtime module offers for per-user messages) so other tabs of the same user refresh their picker and tables.

### Blazor UI (`Coworkee.Client.Blazor`)

- **Groups page** (`/admin/groups`, same permission, same nav entry) becomes a two-pane management page:
  - Left: lazy tree (MudEx tree view as the nav menu uses it, or MudBlazor `MudTreeView` with server data), search box (`SearchGroupTree`), counts per node, "New root group", context actions per node (new sub-group, rename, move, delete).
  - Right: details of the selected group in tabs: **General** (name, description, parent with a "Move" dialog containing a tree picker), **Members** (`CoworkeeDataTable` over users filtered to the group, add users via a user picker dialog, remove, "move all members to…"), **Roles** (existing multi-select, unchanged behaviour), then **app tabs**.
  - Flat apps see their groups as roots; nothing else changes for them.
  - Deep link `?group={id}` selects and expands to the group (global search result URL changes to it).
- **App tabs**: client-side registration, e.g. `services.AddGroupDetailTab<TComponent>("Organisation codes", permission)`; the component receives the group id. CoShipee uses it for org codes, owners, notification recipients.
- **Picker** `GroupScopePicker`: app-bar component the app places itself (not in the template layout by default). Shows the user's accessible groups as a tree with checkboxes (members bold, sub-units below), "All my units" when nothing is selected, a short label of the selection otherwise ("2 units" / unit name). Hidden when the user has no accessible group. On change: `SelectGroups`, then reload data tables and client entities (event on a scoped `GroupScopeState` that `CoworkeeDataTable` listens to).
- **User detail page**: group chips show the path ("Customer A / Hamburg").
- Texts via `L[...]`, all 23 languages, `.razor` + `.razor.cs`.

### Import/export

- The `Groups` OData set gets `ParentId`/`Code` and works with the existing export.
- `AddODataImport` for groups with a row type `(Name, Description, ParentPath)` where `ParentPath` is the slash-joined path of parent names ("Customer A/Hamburg"); missing parents are an error row, not created implicitly (dry run shows them). Upsert by path. Members are not part of the group import (users import exists separately).

### Docs, tests, migrations

- Docs: `security/permissions` or `features/identity` (EN + `.de.md`) section "Group tree and scope" with the filter example; changelog under Unreleased (both languages).
- Tests (`Coworkee.Identity.Tests`, real DB providers as the suite does): code generation for roots/children, move with subtree code rewrite, move into own subtree rejected, depth limit, sibling name clash, delete subtree with grants, member add/remove/move, scope: memberships/accessible/selected/active incl. empty selection = all, selection of a non-accessible group rejected, tenant separation of tree, selection and scope; client tests for the picker and the page (bUnit, as the existing admin pages do).
- Template initial migration regenerated (`templates/Coworkee.Templates/update-migrations.ps1`); demo, Sharemee and HR get a migration `GroupTree`; CoShipee picks it up after `coworkee update`.

## Left to the builder

- How the code segments are allocated (max sibling + 1 is fine; gaps after deletes are fine).
- The migration technique for existing codes (SQL per provider vs. startup fix-up), as long as all three providers end up identical.
- Tree component choice (MudEx vs. MudBlazor), dialog layouts, exact endpoint paths and DTO names within the module's conventions.
- Whether `SearchGroupTree` and `GetGroupChildren` are one query with an optional search.

## Calls made by the author (owner did not decide these; check while reading)

1. Delete removes the whole subtree (CargonerdsApp/ABP behaviour) rather than refusing while children exist.
2. Name unique among siblings, not per tenant.
3. A selection may pick any accessible group (sub-units of memberships too), not only direct memberships; selecting a parent includes its sub-units.
4. Selection stored in its own table (`cw.GroupSelections`), not in user settings (Identity does not reference Settings, and the scope service must read it cheaply).
5. No "is unit" flag on groups; the picker is opt-in by placing the component.
6. New permission `Identity.Groups.ManageMembers` (CargonerdsApp separates managing units from managing their users).
7. Group import addresses parents by name path and does not create missing parents.
8. The selection narrows listings only; access by id, guards and import matching use all accessible groups (builder check Q1).
9. `Identity.Groups.ScopeAll` lets operators pick any group of the tenant (Q2).
10. `IGroupScope` exposes `GroupNode`s and ancestors from the start (Q3).
11. The selection applies to interactive sign-ins only (the assistant in the browser included); API keys, MCP, service clients and jobs see all accessible groups; UI exports capture the selection (Q4).
12. Per-group realtime topics are a follow-up, with a docs warning (Q5).
13. Approvers, mail recipients, resource permissions and group roles stay on direct membership (Q6).
