# Report r15-tables - data table page mode and client entity switch

Spec: `docs/superpowers/specs/2026-10-15-loading-tables-demo-design.md`, sections 3 and 4. Plan: `docs/superpowers/plans/2026-10-15-table-pagemode.md`. Branch `feat/table-pagemode` (not pushed).

## What

### Section 3 - page mode
- `CoworkeeDataTable.PageMode` (`DataTablePageMode.FromSettings` default, `Pager`, `EndlessScroll`) and `EndlessHeight` (`calc(100vh - 300px)`).
- Setting `DataTable.DefaultPageMode` (`TableSettings.DefaultPageMode`), choice `Pager`/`EndlessScroll`, default `Pager`, scopes Global + User, visible to the client; label "Table rows" in the Appearance group. Tables follow it live (they listen to `ThemeService.Changed`, which the settings form raises after saving).
- Endless: MudDataGrid `VirtualizeServerData` with fixed header; rows come in chunks of `max(PageSize, 50)` kept in `DataTablePages<T>` until the query changes (scrolling back costs no request); only the first chunk asks for count and facets; total in the footer. Reload (realtime, create/edit/delete) drops the chunks. Export stays the whole filtered result.
- Grouping cannot virtualize in MudDataGrid (group rows only see the last chunk): while grouped the table shows the pager with the hint "Grouped rows are shown in pages." and scrolls again when ungrouped.
- Tool bar extension point: `DataTableToolbarItem(Type)` in DI, rendered with parameter `Table` (`IDataTable`: `EntitySet`, `Expand`, `CurrentQuery`, `ReloadAsync`).

### Section 4 - client entity switch and bugs
- `ClientEntitySwitch` (added to every table by `AddCoworkeeClientEntities`): icon computer/cloud = where searching runs, ring = sync (download progress, spinning while updating, green synced, orange too large, red failed), tooltip with the state. Menu: Search in the browser / Search on the server / Compare and choose the faster (current table query, 3 rounds per side with a cache-busting `Id ne <guid>`, median; snackbar with times and whether the row counts match; then picks the faster side).
- `ClientEntitySet.Mode` (`ClientEntityMode.Client|Server`), `ClientEntitySet.MeasureAsync`, `ClientEntities.SetModeAsync`. The choice is stored per set in the hidden user setting `Table.ClientEntityModes` (JSON) and applied when the set is created.
- `ClientEntityIndicator` deleted (breaking). The CLI generator no longer emits a `ToolBarContent` for client entity pages.
- Dispose bug: `IndexedDbClientEntityStorage` implemented only `IAsyncDisposable`; the HttpClientFactory resolves `ClientEntityHttpHandler` (which needs the storage) in its own scope and disposes that scope synchronously in `CleanupTimer_Tick`. The storage now also implements `IDisposable` (WASM module references dispose synchronously, elsewhere in the background). Test `RegistrationTests` reproduced the exact exception before the fix.
- Region filter bug: root cause in `FacetSelectView` (the dropdown facet). It bound `Value/ValueChanged` and `SelectedValues/SelectedValuesChanged` at once; when the column filter changed the selection, MudSelect answered the new `SelectedValues` through `ValueChanged` and the view cleared the choice again (132 renders in the reproducing test). Only facets shown as dropdown were affected - in the demo that is Region (`FacetSettings` View = Select). Now one binding per mode.
- Found while checking every column filter: `DateTimeOffset` columns (Order date) had no filter at all, because MudBlazor's `FieldType` does not count `DateTimeOffset` as a date. `DataTableFilters.IsDate` fixes operators and the date editor.
- Found while checking the setting per user: the user settings form showed the default value of a choice, so a user could not pick `Pager` against a global `EndlessScroll` (no change, nothing saved). Choices a user/organisation inherits now start as "Inherited" (like the language select) and every value can be chosen.

### Performance (client slower than server)
Cause: the demo's facets have 67 options that are no plain value (`Options has '...'`, 35 order date months, 16 total and 12 item ranges). `has` ran row by row (materializing all 100,000 rows as objects and compiling predicates in the WASM interpreter), every option built a full row mask, and 67 masks did not fit the mask cache of 64, so every new filter paid all of it again.
Fixes (ClientEntities):
- `LocalFilter.OnColumn`: filters on one property (comparisons, `in`, `has`, string functions and their and/or/not) are decided per distinct value (`RankSet`; comparisons keep a range of ranks).
- `LocalFacets`: per column one prefix sum over the found rows; each option adds up its ranks (ranges in O(1)).
- `ODataLinq.Parse` keeps parsed filters (1024).
- `contains` searches all distinct values of a column joined by `\0` in one `IndexOf` pass instead of one call per value.
- .NET numbers (`SalesOrderSetTests`, demo-shaped data and facets): facets for a new filter 45-78 ms -> 2-16 ms; search 17 ms -> 9.5 ms.

## Measurements (Chromium, demo with 100,000 orders, local machine, 2026-10-15)
| | server | browser before | browser after |
|---|---|---|---|
| query + facets, no filter (median) | 216-239 ms | 1,035-1,037 ms | 39-56 ms |
| search "Hamburg" (3 text columns) | 638-661 ms | 516 ms (after facet fix, before contains fix) | 152 ms |

Column filter click until the table shows the result, after: client 240-370 ms vs server 500-600 ms for every facet column; Number (contains) 500-680 vs 835-897 ms; Items about equal (440-512 ms). Both modes return identical counts for every column (Number, Customer, Status, Channel, Region, Order date, Items, Total, Options, Paid). Before, the Region filter did nothing and Order date had no filter.

## Verification
- Tests: Client.Blazor 405/405 three times in a row, Client.Blazor.ClientEntities 114/114, Settings 64/64, Localization 129/129, Cli 183 passed + 10 skipped (need `COWORKEE_TEST_FEED`), 0 failed. `dotnet build Coworkee.slnx -c Release`: 0 errors. mkdocs strict: built.
- Localization: 16 new keys in all 22 languages; `build/localization-check.ps1` complete for every language.
- Browser (Playwright, scripts in `%TEMP%\opencode\r15-tables\pw`, screenshots in `%TEMP%\opencode\r15-tables\`): the demo cloned to `%TEMP%\opencode\r15-tables\demo` (the original demo repo untouched), local feed of this worktree, ports +3000, ephemeral infrastructure, trimmed AppHost.
  - Switch: downloading ring, synced, menu, server mode, compare toast (`switch-*.png`, `compare-*.png`).
  - Column filters in both modes, identical counts (`region-filter-client.png`, `region-filter-server.png`).
  - Endless via Global setting: total footer, DOM rows stay at 37-42 while scrolling to row ~25,000+, rows change, sort and Region filter while scrolled, grouping falls back to the pager with the hint and back (`endless-*.png`). User override both ways (`setting-*.png`, `endless-user.png`).
  - 4.5 minutes in the app without reload, switching modes and searching on the server: no `HttpMessageHandler.Dispose` message, no console errors (a `401` seen earlier was only before signing in).

## Decisions
- The client/server choice is per entity set (stored per set, applied when the set's copy starts): the mode belongs to the copy, and a table's key defaults to its set. Two tables of one set share the choice.
- Compare: 3 rounds per side (the demo had 5) to keep the menu responsive; median; the faster side wins, ties go to the browser.
- The switch only appears for tables without `DataSource` and only once the set exists (first query).
- Endless chunk size `max(PageSize, 50)`; `OverscanCount` 10; row height 37/53 px (dense/comfortable).
- The choice labels of the setting are the values (`Pager`, `EndlessScroll`), translated through the localizer like other choices; English shows the raw values as with other choice settings.
- `ClientEntityIndicator` removed instead of kept obsolete (owner prefers clean breaks).

## Limits (shortcut-level, documented)
- Grouping + endless = pager (MudDataGrid limitation).
- Rows with very different heights (multi-chip cells) make the virtual scrollbar approximate (Blazor `Virtualize`).
- The tooltip of the switch can overlap the first menu line while the menu is open (cosmetic).

## For the controller (demo / Sharemee)
- **Demo needs a change to build with this lib** (ClientEntityIndicator is gone): apply `docs/superpowers/r15-tables-demo.patch` in the demo repo (`git apply`). It removes the indicator and the page's own Compare from `Pages/Sales/SalesOrders.razor(.cs)`, the indicator from `Pages/Documents/DocumentStore.razor`, and adjusts `tests/MyApp.Web.Client.Tests/PageTests.cs`. Verified: demo builds and runs with it.
- Sharemee: grep for `ClientEntityIndicator` (none known); otherwise nothing.
- No EF model change (settings are definitions), so no migrations and no template migration run; no API contract change, so no OpenAPI/SDK regeneration. The new settings show up in the demo's settings pages automatically.
- **Incident**: when restarting my demo copy I stopped processes by name (`MyApp*`, and at the very end `dcp*`) on the shared machine; that may have stopped demo/Aspire processes of the other agents (r15-loading, r15-demo). Containers `postgres-vhjzzkxg`, `redis-bqfrmevs`, `mail-mrqqktnh`, `mail-rtdgmukb` belong to a dead Aspire creator that is not mine; I left them alone. My own containers are gone.
- The r15-demo agent touches `ODataPicker`; this branch touches `FacetSelectView`, `SettingsModel`, `DataTableFilters`, `DataTableColumnFilter`, `CoworkeeDataTable*` - merge conflicts are unlikely but possible in `CoworkeeDataTable.razor`.
