# Plan 2026-10-15 - data table page mode and client entity switch

Spec: `docs/superpowers/specs/2026-10-15-loading-tables-demo-design.md`, sections 3 and 4. Branch `feat/table-pagemode`.

1. **Disposal bug** (test first): a scope that resolved `ClientEntityHttpHandler` (HttpClientFactory handler scope) disposes synchronously without throwing; `IndexedDbClientEntityStorage` gets a synchronous `Dispose`.
2. **Setting** `DataTable.DefaultPageMode` (`Pager`/`EndlessScroll`, default `Pager`, Global + User, visible to the client) in `TableSettingDefinitions`, name in `TableSettings`; localized display name, description and choices.
3. **Page mode**: `DataTablePageMode { FromSettings, Pager, EndlessScroll }` parameter. Endless uses MudDataGrid `VirtualizeServerData` with a fixed height; rows come in page-sized chunks kept until the query changes (`DataTablePages`), facets and the count only with the first chunk of a query. Grouping falls back to the pager with a hint; the footer keeps the total. `FromSettings` follows the client setting live (ThemeService.Changed). Tests: chunking unit test, bUnit for endless/pager/fallback.
4. **Client entity switch**: toolbar extension point in `Coworkee.Client.Blazor` (`DataTableToolbarItem` services rendered with the table context), `ClientEntitySwitch` in ClientEntities (icon client/server, sync ring, menu Client/Server/Compare, snackbar with the times, picks the faster), mode per set remembered in the user setting `Table.ClientEntityModes`. `ClientEntityIndicator` goes (breaking). Tests: bUnit for menu/compare, set honours server mode.
5. **Perf + Region filter**: reproduce in the demo (clone, local feed, ports +3000), profile the local query path in the browser, fix hot spots with tests in `LargeSetTests`/`LocalTableTests`; fix the Region column filter at its cause and check every column filter type in both modes.
6. Docs EN + DE, localization (22 languages), CHANGELOG if present, report `docs/superpowers/r15-tables-report.md`.
