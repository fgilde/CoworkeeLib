# Round 2026-10-15 – loading screen, media from files, data table page mode, client entity switch, demo forms

Owner request (after release 0.0.1.23). The owner opted out of questions; decisions marked *(call)* are the designer's and may be overridden.

## 1. Loading screen (lib + template)

**Intent:** a great looking app loading screen that matches the current theme and logo, shows the real Blazor WebAssembly load progress and has a nice (background) animation; optionally the sign-in background image/video is used behind it.

- Shown while the WebAssembly runtime loads (and on the first interactive render) in every app built on the lib (template app, demo, Sharemee, generated apps) without per-app markup beyond what the template already has. Lives in the lib (`Coworkee.Client.Blazor` static assets + a server-rendered component the web host's `App.razor` places, or the existing layout host) *(call: builder picks the hook; it must work for the template's render mode and with prerendering)*.
- Colours (primary/secondary/background, dark/light per the user's or system preference) and the logo come from the current default theme (the same data `/api/v1/themes/current` serves anonymously) and are rendered server-side into the page so there is no flash of a wrong theme.
- Real progress: Blazor sets `--blazor-load-percentage` / `--blazor-load-percentage-text` on the document while loading WebAssembly resources; the screen shows a progress ring/bar and the percentage from these values. When the values are not available (server render mode, cached runtime), it shows an indeterminate animation and disappears as soon as the app is interactive.
- Animation: animated logo (subtle pulse/draw-in), animated background (soft moving gradient blobs/particles in theme colours); respects `prefers-reduced-motion` (static fallback). No external assets, no JS framework.
- Background choice *(call)*: theme setting **Loading screen** on Admin → Themes (same place as the sign-in design): `Animated` (default), `SignInMedia` (uses the sign-in design's image or video, with a theme-coloured overlay, falls back to Animated when no media), `Plain`. Stored with the sign-in design / theme settings the same way the sign-in design is stored; localized.
- Fades out smoothly; never blocks longer than the app needs.

## 2. Media from the file manager (lib)

- The theme **logo** and the **sign-in media** (image/video address) can additionally be picked from the app's files with the lib's file picker (`CoworkeeFilePicker` / `FilePickerDialog`), next to the existing address field / upload.
- The logo is stored as a data URL like today (picked file is read and converted; size limit stays 512 KB; SVG sanitising stays) *(call)*.
- Sign-in media must be reachable anonymously: only files that are public can be chosen (or the picker offers to make the chosen file public), the stored value is the file's public URL *(call)*.

## 3. Data table page mode (lib)

- `CoworkeeDataTable` gets a parameter `PageMode` of type `DataTablePageMode { FromSettings, Pager, EndlessScroll }`, default `FromSettings`.
- New setting `DataTable.DefaultPageMode` (choice `Pager`/`EndlessScroll`, default `Pager`, scopes Global + User *(call: users may override)*), shown in the app settings next to the other table settings (`TableSettingDefinitions`). Tables with `FromSettings` follow it (live when changed).
- `EndlessScroll`: rows load page by page while scrolling, rendered with virtualization (MudDataGrid `Virtualize` + server data loading in chunks), works with sorting, filters, facets, grouping (if grouping can't virtualize, grouping switches to pager with a hint *(call)*), selection, realtime updates, export (export still exports the whole filtered result), and with client entities (local data).
- Total count stays visible (footer) in endless mode.

## 4. Client entity mode switch (lib)

- For entities with client entities (local IndexedDB copy), the sync status, the client/server choice and the comparison move into one control in the table toolbar: an icon showing where searching/filtering currently runs (client or server) with the sync status as a ring around it (syncing progress, synced, error). Click opens a dropdown: choose Client or Server, and a **Compare** button.
- Compare runs the same query on both, measures, shows the result in a toast (snackbar: times for client and server, row counts match or not) and then selects the faster mode automatically; the choice is remembered per table (user-level, like other table preferences).
- Replaces the current scattered indicator/compare UI (`ClientEntityIndicator` etc.).
- Fix the reported problems: client mode slower than server on Sales orders (profile and fix the hot path: IndexedDB reads, LINQ over many rows, facets – target: client faster than server for the demo's ~100k orders, or explain the limit), the **Region** column filter not working (and check every column filter type in client and server mode), and the console error `HttpMessageHandler.Dispose() threw ... InvalidOperationException: AsyncDisposableServiceDispose, IndexedDbClientEntityStorage` (a scoped service that only implements `IAsyncDisposable` is disposed synchronously by the HttpClientFactory handler scope – fix the registration/disposal properly).

## 5. Demo forms (demo + lib where the cause is in the lib)

- Products (create and edit): the image is picked with `MudExUploadEdit` (like the theme logo), not the current "choose image" button.
- Products: the **Brand** picker (`ODataPicker`) in the ObjectEdit is misaligned (label row and input not aligned with the other fields, appears above/right). Sales orders: the edit dialog is badly misaligned (customer picker without label and shifted left out of the grid, fields not in the two-column grid). Find the root cause (most likely `ODataPicker`/`OptionalODataPicker` rendering or their wrapping in ObjectEdit) and fix it in the lib so every app benefits; verify both dialogs in the browser.

## Non-goals
- New table features beyond page mode; changes to the AirWorks client (it gets the setting through the API automatically if it reads it; no AirWorks UI work).

## Constraints
- Owner rules in `CONTINUE.md` (no attribution in commits, small files, `.razor` + `.razor.cs`, localization in all 22 languages, docs EN + DE, mkdocs strict).
- Template migration script / demo + Sharemee migrations and OpenAPI/SDK if contracts or the EF model change.
- Browser verification with Playwright for every visible change (screenshots).
