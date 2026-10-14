# AirWorks client – report

Branch `feat/airworks` (worktree `C:\dev\privat\github\wt\airworks\coworkee`), commits `c09ec63..HEAD` on top of `3d490db`. Nothing pushed or published.
AirWorks repo: no change needed, so no `feat/coworkee` branch was created. MudBlazor.Extensions: untouched (uses the published `mudex-webcomponents@9.11.3` bundle).

## What was built
- **About dialog** (Blazor): Nextended shows its version (its assemblies state none; the library records the resolved package version at build time as `AssemblyMetadata("PackageVersion:Nextended.Core")`), the credit tiles no longer print their URL (still links), `AboutCredit.IconBackground` + `AboutCredit.AirWorks(version)` with the official logo on #35496b.
- **Coworkee.Bff**: `BffOptions.CookieName` – a second client on localhost needs its own session cookie (cookies ignore ports).
- **Coworkee.Aspire**: `AddAirWorks<T>(suffix = "airworks")` – shares the BFF wiring with `AddWeb` (refactored into `BffClient`), registers `{app}-airworks` as the next auth-server client, own cookie name, Redis; `AddProjects` recognises `*_AirWorks`.
- **TypeScript packages** in `typescript/` (npm workspaces):
  - `@coworkee/client`: OpenAPI snapshot of the library API (generated app + modules it lacked merged from a second document; `npm run openapi`, `npm run generate`), `createCoworkeeClient` (openapi-fetch), `apiRequest`, `ApiError`, BFF/theme/texts/OData helpers.
  - `@coworkee/airworks`: `useCoworkee(AirWorks, options)` (BFF auth provider, permissions from `/permissions/me`, theme → CSS variables + AirWorks system color, logo/favicon, texts of the user's language, probed apps registered before the desktop starts, initial shortcuts), apps: profile, settings (user/organisation/system from the definitions, conditions, only changes sent), files (folders, upload incl. drag & drop, rename, delete, download), file preview with `<mudex-file-display>` (MudEx web components loaded on demand), data (all OData sets as paged/sortable/searchable tables), `entityApp(...)`, notifications, API docs (Swagger). Styles use the theme's `--cw-*` variables.
- **Template** option `airworks`: `src/MyApp.AirWorks` (host + `ClientApp` with `main.ts` the app owns, Products/Brands entity apps with samples), slnx, app host line + reference, About credit in the web client, LOGIN/README/CI (setup-node). The template pack runs `npm pack` and ships the tarballs in `ClientApp/vendor` (shortcut until npm).
- **CLI/Studio**: catalog option `airworks` with new `CliOption.InfoUrl`; `coworkee options` prints it (also in `--json`); Studio option cards show "More about …" (opens the AirWorks site), summary lists the project, review row, "Everything" preset includes it; German texts.
- **Localization**: the apps reuse library keys; one new key ("API documentation") in all 22 languages (`build/localization-check.ps1` clean for all).
- **Docs** EN/DE: new page *Web → AirWorks client*, rows/sections in CLI, Studio, Aspire (roles + conventions), SDKs (`@coworkee/client`), nav translation.

## Decisions (owner opted out of questions)
1. AirWorks host = BFF like the Blazor web host, registered at the Coworkee auth server; Keycloak is reached through the auth server (same realm, users, sign-in mode). No tokens in the browser, no CORS needed.
2. Packages live in this repo under `typescript/`; not published, shipped as tarballs inside the template; AirWorks from GitHub at a pinned commit.
3. Option in the *General* group (it is a client, not infrastructure), off by default.
4. Library apps reuse existing texts; AirWorks' own chrome uses its en/de locale.
5. Desktop profile stays in localStorage; data app reads OData directly (no client-entity cache).

## Verification
- Unit/component tests: Client.Blazor 305/305, Bff 30/30, Aspire 57/57, Studio 276 passed (1 skipped), Localization 129/129, `typescript` node tests 16/16, `tsc` builds of both packages.
- `dotnet build Coworkee.slnx -c Release`: 0 errors.
- CLI tests with `COWORKEE_TEST_FEED` (real app builds incl. `Generated_app_with_airworks_builds_its_client`): 190 tests, 189 passed on the first run; the one failure was the offline docs page list of the MCP docs search missing the new page (fixed, `McpTests` 7/7 again). `Generated_app_with_airworks_builds_its_client` and the template content tests pass.
- Generated app (`--airworks --keycloak --files`, Postgres, Redis) in Aspire, HTTP script through the AirWorks host (https://localhost:7403 in the run, ports shifted because another app host used the defaults):
  - anonymous: `/` 200, `/bff/user` anonymous, `/api/v1/themes/current` 200, texts 200, `/icons/logo-air.png` 200, `/health` 200;
  - `/bff/login` → auth server `connect/authorize?client_id=shop-airworks` → login page with Keycloak button;
  - password sign-in **and** Keycloak sign-in (realm `shop`, form_post back to the auth server) both land on `/` with `/bff/user` authenticated (Administrator, 49 permissions);
  - settings/client, settings definitions, identity/me, create folder, upload, list, download content, delete, OData service document + `Products` with count, notifications, Swagger: all 200/204; logout → end-session, session gone.
- `python -m mkdocs build --strict`: clean.

## Not verified / open follow-ups
- No browser run of the desktop UI (no browser automation available): rendering, window behaviour, MudEx preview inside a window and theme look are untested in a real browser.
- Publish `@coworkee/client` / `@coworkee/airworks` to npm (then replace the tarballs by a version range) and add an npm step to the release workflow.
- More apps: chat, reports, AI assistant, administration (users, roles, themes, languages), realtime notifications.
- Desktop profile on the server (user setting), vite dev server with HMR inside Aspire, `coworkee doctor` Node.js check.
- npm records git dependencies as `git+ssh` in lock files; without SSH keys npm falls back to https for the public AirWorks repo.
