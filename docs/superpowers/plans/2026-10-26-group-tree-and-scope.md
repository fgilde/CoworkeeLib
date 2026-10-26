# Group tree and group scope Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Groups become a tree per tenant, and apps get a per-user "active groups" scope (with a picker) to filter their data by organisation unit.

**Architecture:** `UserGroup` gets `ParentId` + materialised `Code` path; tree commands live next to the existing group handlers in `Coworkee.Identity/Groups`. `IGroupScope` (interface in `Coworkee.Application`, implementation in Identity) computes memberships/accessible/selected/active ids per request; an AsyncLocal switch `GroupScope` widens the selection where the library checks access instead of listing. Blazor gets a tree management page, an app-bar picker and a detail-tab extension point.

**Tech Stack:** .NET 10, EF Core (Postgres, SQL Server, SQLite), ASP.NET Core minimal APIs, OData (Nextended), Blazor WASM + MudBlazor + MudBlazor.Extensions, xUnit v3 + Shouldly, bUnit.

**Spec:** `docs/superpowers/specs/2026-10-26-group-tree-and-scope-design.md` (read it first; its "Calls made by the author" list is binding).

## Global Constraints

- Work in a worktree `C:\dev\privat\github\wt\group-tree` on branch `feat/group-tree` from `main` of `C:\dev\privat\github\coworkee`. Commits without Co-Authored-By or any attribution line.
- Code style of the repo: CCD, small files, few comments, `.razor` + `.razor.cs`, `// ponytail:` marks deliberate shortcuts with their ceiling. Docs never mention "ABP" or "CleanArchitectureBase".
- Existing endpoints and DTOs change only additively (`GroupRequest` gets a trailing optional `Guid? ParentId = null`, `GroupDto` gets trailing `Guid? ParentId = null, string? Code = null`).
- Code path: 5-digit segments joined by `.`, max length 95 (depth 16). Roots `00001`, `00002`, …; child = parent code + `.` + (max sibling segment + 1).
- Name unique among siblings (`(TenantId, ParentId, Name)`, nulls not distinct; on SQLite enforce in the handler, the index may stay non-unique there).
- Permission constants: `Identity.Groups.ManageMembers`, `Identity.Groups.ScopeAll` in `IdentityPermissions.Groups`; `Manage` implies `ManageMembers`.
- Every new UI text through `L["…"]` and present in all 23 language files of `src/Coworkee.Localization/Localization/*.json` (`build/localization-check.ps1 -Lang xx`, `ShippedLanguagesTests`). Real umlauts in German.
- Build: `dotnet build Coworkee.slnx -c Release`; tests per project `dotnet test --project tests/<Project> -c Release` (Docker for DB providers; `COWORKEE_TEST_DATABASE=SqlServer|Sqlite` for provider runs).
- Model change ⇒ `pwsh templates/Coworkee.Templates/update-migrations.ps1` in the task that changes the model.

## Review Focus

- An existing app with flat groups migrates: all groups get codes on the migrations host run, nothing else changes (Task 1 test `Missing_codes_are_filled_in_name_order`).
- A user selects a group, an admin then removes them from it: the stale selection is ignored, `ActiveAsync` falls back to accessible (Task 5 test `Selection_outside_access_is_ignored`).
- Two users with the same group names in two tenants: tree, children, search, selection and scope never mix (Task 4/5 tenant tests).
- Moving a group with a selected sub-group under another member's group: selection stays valid only while accessible (covered by the same scope test after a move, Task 5).
- Deep link to a record of a non-selected unit while a selection is active opens (by-key unnarrowed) but the list does not show it (Task 6 test `By_key_read_ignores_the_selection`).

---

### Task 1: Tree model, codes and create under a parent

**Files:**
- Modify: `src/Coworkee.Identity/Domain/IdentityEntities.cs` (`UserGroup`: `public Guid? ParentId { get; set; }`, `public string? Code { get; set; }`; new `public sealed class GroupSelection { Guid UserId; Guid GroupId; }`)
- Modify: `src/Coworkee.Identity/Persistence/IdentityModelContributor.cs` (Groups: self FK `ParentId` Restrict, `Code` max 95, unique `(TenantId, Code)`, unique `(TenantId, ParentId, Name)` nulls not distinct replacing `(TenantId, Name)`; table `GroupSelections` key `(UserId, GroupId)`, cascade from `User` and `UserGroup`)
- Create: `src/Coworkee.Identity/Groups/GroupCodes.cs` (`internal static class GroupCodes`: `Segment(int n)`, `Child(string parent, int n)`, `NextAsync(DbSet<UserGroup>, string? parentCode, Guid? parentId, ct)`, `IsDescendant(string code, string ancestor)`, `public static Task FillMissingAsync(CoworkeeDbContext db, CancellationToken ct)`)
- Modify: `src/Coworkee.Identity/Groups/GroupHandlers.cs` (`CreateGroupHandler`: parent lookup in tenant, `Code` allocation, sibling name check; `UpdateGroupHandler`: sibling name check)
- Modify: `src/Coworkee.Contracts/Identity/IdentityDtos.cs` (`GroupRequest(..., Guid? ParentId = null)`, `GroupDto(..., Guid? ParentId = null, string? Code = null)`)
- Modify: `src/Coworkee.Identity/Setup/CoworkeeMigrations.cs` (call `GroupCodes.FillMissingAsync` right after `MigrateAsync`)
- Test: `tests/Coworkee.Identity.Tests/GroupTreeTests.cs`

**Interfaces:**
- Produces: `UserGroup.ParentId`, `UserGroup.Code`, `GroupSelection`, `GroupCodes.*`, `GroupErrors.ParentNotFound` (`identity.group_parent_not_found`, NotFound), `GroupErrors.TooDeep` (validation), `GroupErrors.NameTaken` reused for sibling clashes.

- [ ] **Step 1: Write failing tests** in `GroupTreeTests` (fixture as `GroupAndResourceTests`: `ResetAllAsync`, `SetupAsync`, `Admin` client):
  - `Roots_and_children_get_path_codes`: create `A`, `B`, child `A1` of A, child `A2` of A ⇒ codes `00001`, `00002`, `00001.00001`, `00001.00002` (read via `GET /api/v1/identity/groups`, `GroupDto.Code`/`ParentId`).
  - `Same_name_is_allowed_for_cousins_but_not_siblings`: `A/X` and `B/X` succeed; second `A/X` ⇒ 409 `identity.group_name_taken`; second root `A` ⇒ 409.
  - `Unknown_or_foreign_parent_is_rejected`: parent id of another tenant ⇒ 404 `identity.group_parent_not_found`.
  - `Depth_is_limited_to_16`: chain of 16 succeeds, 17th ⇒ 400.
  - `Missing_codes_are_filled_in_name_order`: insert three groups with `Code = null` directly through the DbContext (names `c`, `a`, `b`), run `GroupCodes.FillMissingAsync` ⇒ `a`=`00001`, `b`=`00002`, `c`=`00003`; existing codes untouched.
- [ ] **Step 2: Run** `dotnet test --project tests/Coworkee.Identity.Tests -c Release --filter GroupTreeTests` ⇒ FAIL (no `Code`).
- [ ] **Step 3: Implement** the model, mapping, `GroupCodes`, handler changes and the migrations hook. `FillMissingAsync` assigns codes per tenant to `Code == null` rows with `ParentId == null`, ordered by name, after the highest existing root segment (legacy rows are all roots).
- [ ] **Step 4: Run** the filter again ⇒ PASS; run the whole `Coworkee.Identity.Tests` ⇒ PASS (existing group tests unchanged).
- [ ] **Step 5: Regenerate the template migration** (`pwsh templates/Coworkee.Templates/update-migrations.ps1`) and build the solution.
- [ ] **Step 6: Commit** `feat(identity): groups form a tree with path codes`.

### Task 2: Move and delete subtrees

**Files:**
- Create: `src/Coworkee.Identity/Groups/GroupTreeHandlers.cs` (`MoveGroup(Guid Id, Guid? ParentId) : ICommand<Result>` `[RequiresPermission(Groups.Manage)]` + handler)
- Modify: `src/Coworkee.Identity/Groups/GroupHandlers.cs` (`DeleteGroupHandler` deletes the subtree deepest first, with `PermissionGrant`s, `ResourcePermission`s and `GroupSelection`s of every removed group)
- Modify: `src/Coworkee.Identity/IdentityEndpoints.Groups.cs` (`PUT /groups/{id}/parent` body `MoveGroupRequest(Guid? ParentId)`)
- Modify: `src/Coworkee.Contracts/Identity/IdentityDtos.cs` (`MoveGroupRequest`)
- Test: `tests/Coworkee.Identity.Tests/GroupTreeTests.cs`

**Interfaces:**
- Consumes: `GroupCodes` (Task 1).
- Produces: `MoveGroup`, `MoveGroupRequest`, `GroupErrors.MoveIntoOwnSubtree` (validation `identity.group_move_cycle`).

- [ ] **Step 1: Failing tests:**
  - `Move_rewrites_the_codes_of_the_subtree`: A(00001)/A1/A1a, B(00002); move A1 under B ⇒ A1 `00002.00001`, A1a `00002.00001.00001`, A1a `ParentId` unchanged.
  - `Move_to_root_appends_a_root_segment`: move A1 to `null` ⇒ `00003`.
  - `Move_into_own_subtree_is_rejected`: move A under A1a ⇒ 400 `identity.group_move_cycle`; move A under A ⇒ 400.
  - `Move_rejects_sibling_name_clash_and_too_deep_targets`.
  - `Delete_removes_the_subtree_with_its_grants`: A/A1/A1a with a group permission grant on A1a and a member on A1 ⇒ after delete A, `GET /groups` empty, `PermissionGrant` rows of the three gone.
- [ ] **Step 2: Run** ⇒ FAIL.
- [ ] **Step 3: Implement.** Move: load subtree by `Code.StartsWith(old + ".")`, compute new code under target via `GroupCodes.NextAsync`, replace the prefix on every descendant in memory (one unit of work; no `ExecuteUpdate`, so audit/realtime see it).
- [ ] **Step 4: Run** ⇒ PASS.
- [ ] **Step 5: Commit** `feat(identity): move and delete group subtrees`.

### Task 3: Member commands and new permissions

**Files:**
- Create: `src/Coworkee.Identity/Groups/GroupMemberHandlers.cs` (`AddGroupMembers(Guid Id, IReadOnlyList<Guid> UserIds)`, `RemoveGroupMembers(Guid Id, IReadOnlyList<Guid> UserIds)`, `MoveGroupMembers(Guid FromId, Guid ToId)` – all `[RequiresPermission(Groups.ManageMembers)]`, `ICommand<Result>`)
- Modify: `src/Coworkee.Identity/Groups/GroupHandlers.cs` (`SetGroupMembers` permission → `ManageMembers`)
- Modify: `src/Coworkee.Contracts/Identity/IdentityPermissions.cs` (`ManageMembers = "Identity.Groups.ManageMembers"`, `ScopeAll = "Identity.Groups.ScopeAll"`)
- Modify: `src/Coworkee.Identity/Permissions/IdentityPermissionDefinitions.cs` (`ManageMembers` "Manage group members" implies View + Users.View; `Manage` additionally implies `ManageMembers`; `ScopeAll` "Work in any group")
- Modify: `src/Coworkee.Identity/IdentityEndpoints.Groups.cs` (`POST /groups/{id}/members/add` + `/remove` body `IdListRequest`, `POST /groups/{id}/members/move` body `MoveMembersRequest(Guid ToId)`)
- Modify: localization JSON files (permission display names, 23 languages)
- Test: `tests/Coworkee.Identity.Tests/GroupMemberTests.cs`

**Interfaces:**
- Produces: the three commands, `MoveMembersRequest`, permission constants.

- [ ] **Step 1: Failing tests:** `Add_and_remove_members_keep_the_others`, `Move_members_merges_into_the_target_without_duplicates`, `Foreign_tenant_user_is_rejected` (400), `ManageMembers_alone_can_change_members_but_not_the_tree` (role with only `ManageMembers`: add ⇒ 2xx, create group ⇒ 403), `Manage_implies_ManageMembers`.
- [ ] **Step 2: Run** ⇒ FAIL.
- [ ] **Step 3: Implement**, texts in all languages.
- [ ] **Step 4: Run** `Coworkee.Identity.Tests` and `tests/Coworkee.Localization.Tests` ⇒ PASS.
- [ ] **Step 5: Commit** `feat(identity): group member commands, ManageMembers and ScopeAll permissions`.

### Task 4: Tree queries and OData fields

**Files:**
- Create: `src/Coworkee.Identity/Groups/GroupTreeQueries.cs` (`GetGroupChildren(Guid? ParentId) : IQuery<Result<IReadOnlyList<GroupNodeDto>>>`, `SearchGroupTree(string Search) : IQuery<Result<IReadOnlyList<GroupNodeDto>>>`, both `[RequiresPermission(Groups.View)]`)
- Modify: `src/Coworkee.Contracts/Identity/IdentityDtos.cs` (`GroupNodeDto(Guid Id, Guid? ParentId, string Code, string Name, string? Description, int ChildCount, int MemberCount)`)
- Modify: `src/Coworkee.Identity/IdentityEndpoints.Groups.cs` (`GET /groups/children?parentId=`, `GET /groups/tree?search=`)
- Modify: `src/Coworkee.Identity/Groups/GroupHandlers.cs` (`GetGroups` fills `ParentId`, `Code`)
- Modify: `src/Coworkee.Identity/OData/IdentityODataSets.cs` (group search result URL `/admin/groups?group={id}`)
- Test: `tests/Coworkee.Identity.Tests/GroupTreeQueryTests.cs`

**Interfaces:**
- Produces: `GroupNodeDto`, the two queries and routes.

- [ ] **Step 1: Failing tests:** `Children_are_listed_by_name_with_counts` (root list for `parentId` absent; counts of direct children and members), `Search_returns_matches_with_their_ancestors` (search `ham` over `Customer A/Hamburg/Port` ⇒ nodes Customer A, Hamburg; order root first), `Search_is_capped_at_200_matches`, `Tree_queries_stay_in_the_tenant` (second tenant via `app.As(otherAdmin, otherTenant)` sees nothing of the first), `OData_groups_expose_parent_and_code` (`GET /odata/Groups?$select=Name,ParentId,Code`).
- [ ] **Step 2: Run** ⇒ FAIL.
- [ ] **Step 3: Implement** (search: `Name.ToLower().Contains`, take 201, ancestors via codes: every prefix of the match codes in one query).
- [ ] **Step 4: Run** ⇒ PASS.
- [ ] **Step 5: Commit** `feat(identity): lazy group tree and tree search`.

### Task 5: Group scope service, selection and "my groups"

**Files:**
- Create: `src/Coworkee.Application/Groups/IGroupScope.cs` (interface + `GroupNode` record exactly as in the spec)
- Create: `src/Coworkee.Application/Groups/GroupScope.cs` (`public static class GroupScope`: `IDisposable Unnarrowed()`, `IDisposable Selection(IReadOnlyList<Guid> ids)`, `internal`/public read accessors `IsUnnarrowed`, `SelectionOverride`; AsyncLocal, nesting restores the previous value)
- Create: `src/Coworkee.Identity/Groups/GroupScopeService.cs` (`internal sealed class GroupScopeService(CoworkeeDbContext db, ICurrentUser user, IPermissionChecker permissions, IHttpContextAccessor http) : IGroupScope`, registered scoped in `CoworkeeIdentityModule`)
- Create: `src/Coworkee.Identity/Groups/GroupScopeHandlers.cs` (`GetMyGroups : IQuery<Result<IReadOnlyList<MyGroupDto>>>` signed-in; `SelectGroups(IReadOnlyList<Guid> GroupIds) : ICommand<Result>` signed-in; publishes `RealtimeTopics.User(userId)` type `groups.selection` after commit)
- Modify: `src/Coworkee.Contracts/Identity/IdentityDtos.cs` (`MyGroupDto(Guid Id, Guid? ParentId, string Code, string Name, bool IsMember, bool Selected)`, `GroupSelectionEvents.Changed = "groups.selection"`)
- Modify: `src/Coworkee.Identity/IdentityEndpoints.Groups.cs` (`GET /groups/mine`, `PUT /groups/mine/selection` body `IdListRequest`)
- Test: `tests/Coworkee.Identity.Tests/GroupScopeTests.cs`

**Interfaces:**
- Consumes: `GroupSelection`, `GroupCodes.IsDescendant`.
- Produces: `IGroupScope`, `GroupNode`, `GroupScope.Unnarrowed()`, `GroupScope.Selection(ids)`, `MyGroupDto`, routes. Rules:
  - Accessible = memberships + descendants; with `ScopeAll` every group of the tenant.
  - Selection applies when `GroupScope.SelectionOverride` is set (wins), or when there is an HTTP context, `ApiKeyId == null` and `ClientId == null`. Otherwise selected = empty.
  - Selected = stored (or override) ∩ accessible. Active = (selected + their descendants) ∩ accessible, or accessible when selected is empty or `GroupScope.IsUnnarrowed`.
  - No user ⇒ all lists empty. Results memoised per instance.
  - `GetMyGroups` without `ScopeAll`: all accessible nodes; with `ScopeAll`: memberships, selected groups and their ancestors only (the picker loads the rest lazily through Task 4 routes).

- [ ] **Step 1: Failing tests** (direct service tests use `CurrentUserScope.Begin(new ImpersonatedUser(...))` + a scope as in `Role_ids_of_a_user_come_directly_and_through_groups`; HTTP tests use `app.As`):
  - `Empty_selection_means_every_accessible_group`: member of A and B (A has A1) ⇒ Active = {A, A1, B}.
  - `Selection_narrows_to_the_picked_groups_and_their_subtree`: select A ⇒ Active = {A, A1}.
  - `Selecting_an_inaccessible_group_is_rejected` (400, nothing stored).
  - `Selection_outside_access_is_ignored`: select A, then remove membership ⇒ Active = {B}.
  - `ScopeAll_makes_every_group_accessible_and_selectable`.
  - `Api_keys_and_jobs_ignore_the_selection`: with selection A, a scope with `ApiKeyId` set (test auth with api key claim, or a fake `ICurrentUser`) ⇒ Active = all accessible; without HTTP context ⇒ same; with `GroupScope.Selection([B])` override ⇒ Active = {B}.
  - `Unnarrowed_returns_accessible`.
  - `Nodes_and_ancestors_are_tenant_bound`.
  - `My_groups_flags_members_and_selection`.
  - `Selection_change_is_published_to_the_user_topic` (fake `IRealtimePublisher` as other identity tests do, topic `user:{id}`, type `groups.selection`).
- [ ] **Step 2: Run** ⇒ FAIL.
- [ ] **Step 3: Implement.** At most two queries per request for memberships/accessible (memberships with codes, then descendants by `Code.StartsWith` OR), one for the stored selection. `// ponytail:` note: no cross-request cache.
- [ ] **Step 4: Run** `Coworkee.Identity.Tests` ⇒ PASS.
- [ ] **Step 5: Commit** `feat(identity): group scope with per-user selection`.

### Task 6: Listing vs. access in the library, exports, client entities

**Files:**
- Modify: `src/Coworkee.OData/EntityODataController.cs` (by-key reads compute `_visible` inside `GroupScope.Unnarrowed()`; decide by the action's `key` route value)
- Modify: `src/Coworkee.OData/ODataEntityAccess.cs` (`VisibleIdsAsync` runs unnarrowed)
- Modify: `src/Coworkee.Auditing/History/EntityHistoryRegistration.cs`, `src/Coworkee.OData/Transfer/Import/ImportPlanner.cs`, `src/Coworkee.Workflows/EntityWorkflow.cs` (`VisibleAsync(id)`): unnarrowed
- Modify: export request + `src/Coworkee.OData/Transfer/ExportJob.cs`/`DataExport.cs`: capture `await scope.SelectedAsync()` when the export is requested, store it in the job payload, run the export inside `GroupScope.Selection(ids)` (empty ⇒ no override)
- Create: `src/Coworkee.OData/ClientEntities/IClientEntityPartitionPart.cs` (`public interface IClientEntityPartitionPart { Task<string?> GetAsync(CancellationToken ct); }`); `ClientEntityEndpoints.Partition` appends every non-null part before hashing
- Create: `src/Coworkee.Identity/Groups/GroupScopePartitionPart.cs` (returns `string.Join(",", selected ids sorted)` or null when nothing selected); registered in Identity where OData is present
- Test: `tests/Coworkee.OData.Tests/GroupScopeNarrowingTests.cs` (test entity with a `Guid UnitId` and an `IODataEntityFilter` using `IGroupScope.ActiveAsync`; follow the OData test app's existing test entity registration), `tests/Coworkee.Identity.Tests/GroupScopeTests.cs` (partition)

**Interfaces:**
- Consumes: `IGroupScope`, `GroupScope` (Task 5).
- Produces: `IClientEntityPartitionPart`.

- [ ] **Step 1: Failing tests:** `Collection_follows_the_selection` (rows of A and B, selection A ⇒ `/odata/<set>` returns A rows, `$count` = A count), `By_key_read_ignores_the_selection` (`/odata/<set>(<B row id>)` ⇒ 200), `Global_search_follows_the_selection`, `Export_uses_the_selection_of_the_request` (start export with selection A, change selection to B before the job runs ⇒ file has A rows), `Import_matches_rows_outside_the_selection` (upsert of a B row with selection A updates, no duplicate), `Partition_changes_with_the_selection`.
- [ ] **Step 2: Run** ⇒ FAIL.
- [ ] **Step 3: Implement.** Before editing, list every caller of `VisibleAsync`/`VisibleIdsAsync`/`FilteredAsync` and classify it (listing: collections, `$count`, facets, search, export, `QueryDataHandler`, client entities, reports; access: by-key, guards via `VisibleIdsAsync`, history, import matching, workflow visibility). Put the list in the commit message body.
- [ ] **Step 4: Run** `Coworkee.OData.Tests`, `Coworkee.Identity.Tests`, `Coworkee.Files.Tests`, `Coworkee.Social.Tests`, `Coworkee.Auditing.Tests`, `Coworkee.Workflows.Tests` ⇒ PASS.
- [ ] **Step 5: Commit** `feat(odata): group selection narrows listings, not access`.

### Task 7: Group import by path

**Files:**
- Create: `src/Coworkee.Identity/Groups/GroupImport.cs` (`public sealed record GroupImportRow(string Name, string? Description, string? ParentPath)`; `ImportGroup(Guid? Id, GroupImportRow Row) : ICommand<Result<Guid>>` `[RequiresPermission(Groups.Manage)]`: resolves `ParentPath` ("A/B", names split on `/`, trimmed) from the root down, upserts by (parent, name), missing parent ⇒ validation error `identity.group_parent_path_unknown`)
- Modify: `src/Coworkee.Identity/OData/IdentityODataSets.cs` (`AddODataImport<GroupImportRow, Result<Guid>>("Groups", row => new ImportGroup(null, row))` per the existing `AddODataImport` overloads; delete/undo only if the overload requires it, mapped to `DeleteGroup`)
- Test: `tests/Coworkee.Identity.Tests/GroupImportTests.cs`

- [ ] **Step 1: Failing tests:** `Rows_create_groups_under_existing_parents`, `Rows_in_file_order_can_build_a_tree` (row 1 `A`, row 2 `B` under `A`), `Unknown_parent_path_is_an_error_row`, `Same_path_updates_the_description`.
- [ ] **Step 2: Run** ⇒ FAIL. **Step 3: Implement.** **Step 4: Run** ⇒ PASS.
- [ ] **Step 5: Commit** `feat(identity): import groups by parent path`.

### Task 8: Client API and group management page

**Files:**
- Modify: `src/Coworkee.Client.Blazor/Api/ICoworkeeApi.cs`, `CoworkeeApi.cs` (methods for every new route: `GetGroupChildrenAsync(Guid?)`, `SearchGroupTreeAsync(string)`, `MoveGroupAsync(Guid, Guid?)`, `AddGroupMembersAsync`, `RemoveGroupMembersAsync`, `MoveGroupMembersAsync(Guid from, Guid to)`, `GetMyGroupsAsync()`, `SelectGroupsAsync(IReadOnlyList<Guid>)`; `CreateGroupAsync` passes `ParentId`); every fake/test implementation of `ICoworkeeApi` updated
- Rewrite: `src/Coworkee.Client.Blazor/Pages/Admin/Groups.razor(.cs)` (two panes per spec; deep link `?group=`)
- Create: `src/Coworkee.Client.Blazor/Components/Groups/GroupTree.razor(.cs)` (lazy tree + search, used by the page, the move dialog and the picker), `GroupMoveDialog.razor(.cs)`, `GroupMembersTab.razor(.cs)` (`CoworkeeDataTable` on `Users` with `$filter` on group membership or a member list; add via user picker dialog, remove, move all), `GroupGeneralTab.razor(.cs)`, `GroupRolesTab.razor(.cs)` (existing multi-select)
- Create: `src/Coworkee.Client.Blazor/Components/Groups/GroupDetailTabs.cs` (`public static IServiceCollection AddGroupDetailTab<TComponent>(this IServiceCollection services, string title, string? permission = null) where TComponent : IComponent`; component gets `[Parameter] Guid GroupId`)
- Modify: user detail page group chips show the path (ancestors via `SearchGroupTree`-free lookup: `GetGroupChildren` not needed; use `GroupDto.Code` + loaded group list)
- Texts in all 23 languages
- Test: `tests/Coworkee.Client.Blazor.Tests/GroupsPageTests.cs`

- [ ] **Step 1: Failing bUnit tests** (pattern of `AdminTablesTests`/`ClientTestBase`): `Tree_shows_roots_and_loads_children_on_expand`, `Search_shows_matches_with_ancestors`, `Selecting_a_node_shows_its_tabs`, `App_tab_is_shown_with_the_group_id` (registered via `AddGroupDetailTab`), `App_tab_with_missing_permission_is_hidden`, `Delete_confirms_with_subgroup_and_member_counts`, `Manage_actions_are_hidden_without_Manage`, `Deep_link_selects_the_group`.
- [ ] **Step 2: Run** `dotnet test --project tests/Coworkee.Client.Blazor.Tests -c Release --filter GroupsPageTests` ⇒ FAIL.
- [ ] **Step 3: Implement.** Tree component: `MudExTreeView` as `CoworkeeNavMenu` uses it, or `MudTreeView` with `ServerData`; pick the one that supports lazy children cleanly and note it in the commit.
- [ ] **Step 4: Run** the client tests and the localization tests ⇒ PASS.
- [ ] **Step 5: Commit** `feat(client): group tree management page`.

### Task 9: Group scope picker

**Files:**
- Create: `src/Coworkee.Client.Blazor/Components/Groups/GroupScopeState.cs` (scoped; `IReadOnlyList<MyGroupDto> Groups`, `event Action? Changed`, `LoadAsync()`, `SelectAsync(ids)`; listens to the realtime user topic type `groups.selection` and reloads; on change calls `ClientEntities.ClearAsync()` when the client-entities package is registered (optional service))
- Create: `src/Coworkee.Client.Blazor/Components/Groups/GroupScopePicker.razor(.cs)` (app-bar button with label "All my groups" / one name / "{0} groups", popover with `GroupTree` in checkbox mode over `MyGroupDto`s; hidden when `Groups` is empty)
- Modify: `src/Coworkee.Client.Blazor/Components/Data/CoworkeeDataTable.razor.cs` (subscribe to `GroupScopeState.Changed` when the service exists ⇒ `ReloadAsync()`)
- Register `GroupScopeState` in the client's service registration
- Texts in all 23 languages
- Test: `tests/Coworkee.Client.Blazor.Tests/GroupScopePickerTests.cs`

- [ ] **Step 1: Failing tests:** `Hidden_without_groups`, `Label_shows_all_one_or_count`, `Selecting_sends_the_ids_and_reloads_tables`, `Clearing_resets_to_all`, `Realtime_selection_event_reloads_the_state`.
- [ ] **Step 2: Run** ⇒ FAIL. **Step 3: Implement.** **Step 4: Run** ⇒ PASS.
- [ ] **Step 5: Commit** `feat(client): group scope picker`.

### Task 10: Docs, changelog, full verification, merge

**Files:**
- Modify: `website/docs/features/identity.md` + `identity.de.md` (section "Group tree and scope": tree, permissions, `IGroupScope`, filter example from the spec, listing vs. access, API keys/jobs, export capture, picker placement `<GroupScopePicker />` in the app bar, `AddGroupDetailTab`, import by path, warning about `[Realtime]` broadcasts with units as customers)
- Modify: `website/docs/reference/changelog.md` (`## Unreleased`) + `changelog.de.md` (`## Unveröffentlicht`)
- Modify: `CONTINUE.md` (round section)

- [ ] **Step 1:** Docs + changelog; `python -m mkdocs build --strict -f website/mkdocs.yml -d %TEMP%\cw-docs` ⇒ no warnings.
- [ ] **Step 2:** `dotnet build Coworkee.slnx -c Release` ⇒ 0 errors, no new warnings; `dotnet test --solution Coworkee.slnx -c Release --no-build` ⇒ all pass (note flaky tests separately with their names).
- [ ] **Step 3:** SQL Server provider run of `Coworkee.Identity.Tests` and `Coworkee.OData.Tests` (`COWORKEE_TEST_DATABASE=SqlServer`) ⇒ PASS (CoShipee runs SQL Server).
- [ ] **Step 4:** Commit `docs: group tree and scope`, fast-forward `main`, push `main`.
- [ ] **Step 5:** `pwsh build/pack-local.ps1` ⇒ packages in `artifacts/nuget` for CoShipee's next step.

### Task 11: Downstream apps

**Files:** demo (`C:\dev\privat\github\cleanarchitecturebaseblazor`, `master`), Sharemee (`C:\dev\privat\github\shareme`, `master`), HR (`C:\dev\privat\github\CoworkeeHR`, `master`)

- [ ] **Step 1:** In each app: `dotnet restore --force-evaluate` against the local pack, `dotnet ef migrations add GroupTree` (their infrastructure project, as their AGENTS.md/README says), build.
- [ ] **Step 2:** Run each app's migrations tests (`*.Migrations.Tests`) ⇒ PASS; demo/Sharemee: refresh the OpenAPI snapshot (`MYAPP_UPDATE_OPENAPI=1` / `SHAREME_UPDATE_OPENAPI=1`) and regenerate the TS SDK where the repo keeps one.
- [ ] **Step 3:** Commit `chore: migration GroupTree` in each app (no attribution); push demo and Sharemee `master` (HR has no remote).
