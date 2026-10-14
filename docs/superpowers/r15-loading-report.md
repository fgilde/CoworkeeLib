# Report: loading screen and media from files (round 2026-10-15, sections 1 + 2)

Branch `feat/loading-screen` (worktree `wt/r15-loading`), plan `docs/superpowers/plans/2026-10-15-loading-screen.md`. Not pushed.

## What
- **Loading screen** (`Coworkee.Bff`): `CoworkeeLoadingScreen` (static server component) renders `LoadingScreenMarkup` – inline CSS + small inline script, no assets, no framework. Theme colours (Primary/Secondary/Tertiary/Background/TextPrimary, light and dark) and logo come from the new anonymous `GET /api/v1/themes/loading-screen` (`LoadingScreenDto(Theme, Background, MediaUrl)`), fetched by `LoadingScreenSource` (session token forwarded, cached per organisation 30 s, 2 s timeout, neutral colours on failure). Progress ring + percentage from Blazor's `--blazor-load-percentage(-text)`, spinning ring while there is none, pulsing logo, moving blurred shapes, `prefers-reduced-motion` = static, fade out.
- Hide: `CoworkeeLayout` (once visible and the theme/settings are applied) and `SetupLayout` call `coworkee.js hideLoadingScreen(mode, theme, culture)`, which also writes the cookie `coworkee-loading` (`mode=…&theme=…&culture=…`) so the next server render uses the user's mode, theme and language; without cookie `prefers-color-scheme` and `Accept-Language` decide.
- Background choice: `SignInDesign.LoadingScreen` (`Animated` default, `SignInMedia`, `Plain`), select on the *Sign-in page* card; Aspire forwards `options.SignIn.LoadingScreen`.
- Template `App.razor`: `<Coworkee.Bff.CoworkeeLoadingScreen Logo="coworkee-icon.svg" />`. Template initial migration regenerated (only the EF product version changed: the Files module is optional, the CLI adds its migration).
- **Public files** (`Coworkee.Files`): `StoredFile.IsPublic` (+ `StoredFileDto.IsPublic`), `PUT /api/v1/files/{id}/public` (Files.Manage), anonymous `GET /api/v1/files/public/{id}/{name}` (`FileLinks.Public`, range support, `Cache-Control: public, max-age=300`). `BlobContentResult` takes a cache-control value. Preview of a public file shows a notice with *Make private*.
- **Media from files**: `PublicFileChooser` (picker → confirm "Make public" → absolute public URL); folder button in the sign-in media field. Theme logo dialog: *Choose from files* reads the file and runs the existing pipeline (resize to 512 px, 512 KB check, server SVG sanitising unchanged).
- **Round 2 (limits closed):**
  - *Sharing in the file manager*: bar above the Files page's file manager acting on the selected file – *Make public* (same confirmation, only with `Files.Manage` on the folder, `FilesStructureManager.CanManage`) / *Make private*, *Copy public link*; *Public* chip in the bar and a globe icon on public files in the file area (`FileItemTemplate`). Shared logic in `FilePublishing` (also used by `PublicFileChooser` and the preview). Folders have no public state in the model: files are published one by one (decision; a folder flag would publish whole subtrees unnoticed).
  - *User's theme*: `GET /api/v1/themes/loading-screen?themeId=` – `GetChoosableTheme` returns the theme only for a signed-in caller and only when it is built-in or published in the caller's organisation (independent of the caller's permissions, so no unpublished theme leaks and the BFF may cache per organisation + theme); otherwise the default. The BFF passes the cookie's theme only for signed-in visitors; anonymous visitors always get the default.
  - *Language*: the screen's texts (incl. the screen reader label) come in the cookie's or the browser's language from `/api/v1/localization/texts/{culture}` (cached 10 min per culture, neutral culture as fallback, English without answer) – the former English-only label is gone.
  - *Failed start*: same design switches at once to *The app could not start.* + hint + *Reload* + collapsible *Details* (last console error). Triggers: Blazor showing `#blazor-error-ui` (the screen ships a hidden first one that Blazor finds), framework script/resource load errors, unhandled errors/rejections from `_framework`/blazor/dotnet; `window.coworkeeLoadingScreen.fail(detail)` for apps. `#blazor-error-ui` stays hidden while the message shows; the 30 s safety timeout no longer runs after a failure.
- 13 + 7 new texts in all 22 languages; docs EN+DE (theming, files, sign-in pages), Bff README.

## Decisions
- Hook: explicit component in App.razor (static SSR, works with the template's `prerender: false`) instead of HTML rewriting middleware; apps add one line.
- The screen uses the organisation's **default** theme, not a user's own chosen theme (spec: default theme); colour mode follows the cookie.
- Safety hide 30 s after the download reached 100 % (or 30 s after start when Blazor reports no progress) for apps whose own layout never calls `hide()`; failures are detected separately. The first version counted from page start and hid the screen mid-download on a slow network (found in the browser test, fixed).
- Cookie values are hints only: mode/culture are validated, the theme is checked by the API.
- Publishing needs `Files.Manage` (also for the uploader). Public URL stored absolute on the web app origin, so auth server, Keycloak and loading screen load it anonymously through the BFF.
- `ThemeLogoDialog` now opens via `ShowExAsync`: with a plain MudBlazor dialog open, MudEx's dialog finder took that dialog for the new file picker side sheet, which stayed at size 0 (found in the browser).
- Media URL safety in the markup: same-site paths, relative paths without scheme, or http(s); `//` and `/\` rejected; colours regex-checked; everything HTML-encoded.

## Verification
- Round 2 tests green: Client.Blazor 401 (new `FilesSharingTests`), Bff 48 (preferences, texts, cache per organisation/theme, error markup), Theming 84 (choosable theme: published/built-in yes, draft/other organisation/anonymous/unknown no), Files 8, Localization 129; slnx Release build 0 errors; mkdocs strict ok.
- Tests green (round 1): Client.Blazor 397, Bff 43, Theming 83 (+1 skipped, Unix only), Files 8, Localization 129, Aspire 57, AuthServer 101, AspNetCore 27, Architecture 3, Client 2; CLI with real builds (`COWORKEE_TEST_FEED`) 193/193. `dotnet build Coworkee.slnx -c Release`: 0 errors. mkdocs `--strict` ok.
- Browser (generated app `loaddemo`, sqlite + files, Aspire on https 9300-9302 / http 9310-9312, local packs, Playwright, CDP throttled to 3 Mbit/s with cache off). Screenshots in `C:\Users\User\AppData\Local\Temp\opencode\r15-loading\`:
  - progress steps (indeterminate, 25, 50, ~75, 100 %): `loading-animated-light-*.png`, `loading-animated-dark-*.png`, `loading-plain-dark-*.png`, `loading-video-light-*.png`, `loading-video-dark-*.png`, `loading-image-light-*.png`
  - reduced motion (video frame stays at 0:00): `loading-video-reduced-*.png`
  - admin: `admin-make-public.png`, `admin-sign-in-card.png`, `admin-logo-from-files.png`, `admin-themes-after.png`; sign-in page playing the public video with the logo from files: `signin-page-video.png`
  - round 2: `files-selected-private.png`, `files-public-badge.png` (chip, globe icons, *Copy public link* put `…/api/v1/files/public/{id}/logo-file.png` in the clipboard, anonymous GET 200), `files-made-private.png` (anonymous GET 404 afterwards); `loading-user-theme-sunset-dark.png` (user chose Sunset + dark + German: cookie `mode=dark&theme=…&culture=de`, screen renders Sunset's dark primary `#f48fb1` and `aria-label="Wird geladen"`; the same cookie without sign-in renders the default theme); `loading-failed-fail-light.png` / `-details.png` (all `.wasm` downloads aborted: error state after 252 ms, details show the fetch error, `#blazor-error-ui` not visible, *Reload* starts the app once the network is back), `loading-failed-fail-de-dark.png` (German, dark), `loading-failed-script.png` (blazor.web.js not loadable: error state after 79 ms).
  - logo picked from files appears in the screen; anonymous GET of the public video 200 `video/mp4`; cookie `coworkee-theme-mode=dark` renders only the dark palette (checked over HTTP).

## For the controller (demo / Sharemee)
- Add `<Coworkee.Bff.CoworkeeLoadingScreen Logo="coworkee-icon.svg" />` as the first line of `<body>` in `src/MyApp.Web/App.razor` (demo) and `src/Shareme.Web/App.razor` (Sharemee; pick its own icon or omit `Logo`).
- EF migration for `cw.StoredFiles.IsPublic` (bool, not null, default false) in demo and Sharemee (both use the Files module).
- OpenAPI snapshots + TS SDK: new `GET /api/v1/themes/loading-screen` (`LoadingScreenDto`), `PUT /api/v1/files/{id}/public` (`FilePublicRequest`), `GET /api/v1/files/public/{id}/{name}`, `StoredFileDto.isPublic`, `SignInDesign.loadingScreen` (enum `LoadingScreenBackground`).
- After packing: `dotnet restore --force-evaluate` there.

## Not done / limits
- None in the lib. Outside it: demo/Sharemee need the App.razor line, the migration and the OpenAPI/SDK refresh listed above (the `themeId` query parameter is new in the snapshot too).
