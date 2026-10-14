# AirWorks client for Coworkee apps – design

Date: 2026-10-14 · Branch `feat/airworks` · Owner opted out of questions: every decision below is the agent's call, marked **[call]**.

## Goal

A generated Coworkee app can get a second client next to the Blazor app: a web desktop built on [AirWorks](https://fgilde.github.io/AirWorks/).
It runs in the Aspire app host, signs in through the same auth server (and therefore the same Keycloak realm), talks to the same API,
shows the configured logo and theme, and offers apps for settings, profile, files, data (entity sets) and notifications. The library ships
the building blocks as npm packages; the generated app composes them and can replace or add apps.

Also in scope (independent of the option): the About dialog shows the Nextended version and no longer prints the URL inside the credit buttons;
with the AirWorks option the About dialog of the Blazor app lists AirWorks with its official logo.

## Architecture

```
browser ── AirWorks SPA (vite build, @fgilde/airworks + @coworkee/airworks + @coworkee/client)
   │  same origin, cookie session, X-CSRF: 1
   ▼
MyApp.AirWorks (ASP.NET host: static files + Coworkee.Bff)  ── OIDC code+PKCE ──▶ MyApp.Auth (OpenIddict) ──▶ Keycloak realm (optional)
   │  /api, /odata, /hubs, /openapi, /swagger forwarded with the access token (YARP, as the Blazor web host does)
   ▼
MyApp.Api
```

### Sign-in **[call]**
- The AirWorks host is a **BFF like `MyApp.Web`** (`AddCoworkeeBff`/`MapCoworkeeBff`), not a public SPA client with tokens in the browser.
  Reasons: the API and auth server have no CORS; the BFF already does code+PKCE, refresh, session store, CSRF header, API/OData/hub forwarding
  and sign-out on 401; tokens never reach the browser. "Same Keycloak" follows: the auth server federates to the app's Keycloak realm exactly as
  for the Blazor app (LoginMode, users, roles) – no second Keycloak client is needed because the Blazor app has none either.
- Aspire registers the host as a second client of the auth server: `{app}-airworks`, redirect `/signin-oidc`, post-logout `/signout-callback-oidc`,
  scope `{app}_api`, launcher entry with its address.
- New `BffOptions.CookieName` (`Coworkee:Bff:CookieName`): both hosts run on localhost, and cookies ignore ports – with the same cookie name a
  sign-in in one client would sign the other out. Aspire sets `{app}-airworks` for the AirWorks host; the Blazor host keeps the default name.
- In the SPA, a `bffAuthProvider` implements AirWorks' `AuthProvider`: `login()` → `/bff/login?returnUrl=…`, `restore()` → `GET /bff/user`,
  `logout()` → `POST /bff/logout` and follow the end-session address. Permissions come from `GET /api/v1/identity/permissions/me` (`accessProvider`).

### Packages **[call]**
Folder `typescript/` in this repo (`packages/` is ignored by the NuGet rules of .gitignore), npm workspaces, TypeScript, ES modules, built with `tsc`, tests with `node --test` (Node ≥ 22.18 strips types).

| Package | Content |
|---|---|
| `@coworkee/client` | `openapi.json` snapshot of the library API + types generated with `openapi-typescript` (`src/schema.ts`); `createCoworkeeClient()` (openapi-fetch, same-origin cookie + `X-CSRF` or a bearer token); `bff` helpers (user, login, logout); `ApiError` with problem details; `theme` helpers (palette → CSS variables, logo → image address); `texts` (server texts per culture with English fallback); `odata` (query string builder, service document, page fetch). Framework free, usable from any TS app. |
| `@coworkee/airworks` | `useCoworkee(AirWorks, options)`: auth provider, permissions, brand (title/logo/favicon), theme (desktop system color, CSS variables, color mode), texts, search provider; app factories `settingsApp`, `profileApp`, `filesApp`, `dataApp`, `entityApp`, `notificationsApp`, `apiDocsApp`; MudEx web components loader for file previews (`<mudex-file-display>`). Styles in `style.css`. Peer dependency `@fgilde/airworks`. |

- Snapshot source **[call]**: the OpenAPI document of a generated app with every library module and no samples (`typescript/client/openapi.json`).
  `npm run openapi -- <url>` refreshes it from a running API, `npm run generate` rebuilds `schema.ts`.
- Distribution **[call]**: nothing is published. `dotnet pack` of `Coworkee.Templates` runs `npm pack` for both packages and puts the tarballs into
  the template (`src/MyApp.AirWorks/ClientApp/vendor/*.tgz`); the generated `package.json` references them with `file:`.
  `shortcut: tarballs inside the template until @coworkee/* is on npm, then a version range`.
- AirWorks itself comes from GitHub, pinned to a commit (`git+https://github.com/fgilde/AirWorks.git#<sha>`, its `prepare` builds the package).
  No AirWorks change is required; if one becomes necessary it goes to AirWorks branch `feat/coworkee` (never pushed).

### Generated app (template option `airworks`) **[call]**
- New project `src/MyApp.AirWorks`: Web SDK host (`Program.cs`: service defaults, BFF, static files, SPA fallback; Kestrel body limit like the web host),
  `launchSettings.json` (https 7303 / http 5303), `ClientApp/` (vite + TS: `index.html`, `src/main.ts` composition, `src/styles.css`),
  MSBuild target `BuildClient` (npm install + vite build into `wwwroot`, incremental via Inputs/Outputs, skipped with `-p:SkipClientBuild=true`).
- `main.ts` is the user's: it calls `useCoworkee(...)`, registers the library apps it wants and may add its own (`entityApp` for the sample
  `Products`/`Brands` when `samples` is on). The app logo is imported from the web client's wwwroot so `--logo` reaches it; the theme logo wins.
- App host: `app.AddAirWorks<Projects.MyApp_AirWorks>();` after `AddWeb`. `AddProjects` recognises `_AirWorks`.
- Blazor client: `options.About.Credits.Add(AboutCredit.AirWorks(...))` in `Program.cs` of the web client.
- `LOGIN.md`: one line about the AirWorks client address.
- Option catalog: `airworks` (General group, bool, default false, flag `--airworks`), new `CliOption.InfoUrl` = https://fgilde.github.io/AirWorks/.
  Studio: the option card shows a "More about AirWorks" link when `InfoUrl` is set; summary lists project `MyApp.AirWorks`. `coworkee options` prints the URL.
- The project needs Node.js ≥ 22.18 to build (README/docs say so; a `coworkee doctor` check is a follow-up).

### Aspire
`CoworkeeApp.AddAirWorks<TProject>(string suffix = "airworks")` – the BFF settings as for `AddWeb` (authority, client id `{name}-{suffix}`, api address,
scopes, forwarded prefixes `/hubs`, cookie name), Redis wiring, the next auth client slot (`Clients[n]`). Shared code with `AddWeb` moves into one private method.

### Apps (phase 1 = vertical slice, phase 2 = broaden)
| App | Phase | Backend |
|---|---|---|
| Settings (user settings by definitions; admins: organisation/global) | 1 | `/api/v1/settings/definitions[/user]`, `/settings/{user,tenant,global}` |
| Profile (name, phone) + sign out | 1 | `/api/v1/identity/me` |
| Files (folders, upload, rename, delete, download, preview with `<mudex-file-display>`) | 1 | `/api/v1/files/...` |
| Data (all OData entity sets, table with paging, sorting, search) | 1 | `/odata` service document, `$top/$skip/$count/$orderby/$filter` |
| `entityApp(set, …)` – one desktop app per entity set | 1 | `/odata/{set}` |
| Notifications (list, mark read) | 2 | `/api/v1/notifications` |
| API documentation (Swagger UI in a window) | 2 | `/swagger` through the BFF |
| Chat, reports, AI assistant, administration (users, roles, themes, languages) | follow-up | – |

### Theme and texts
- `GET /api/v1/themes/current` (anonymous): palette of the color mode (`Theme.Mode` user setting / system) → CSS variables `--cw-*` on `:root`,
  AirWorks `systemColor` = primary, brand logo = `theme.logo` (SVG markup → data URL, data URL as is) or the app logo.
- Texts: `GET /api/v1/localization/texts/{culture}` for the user's language (`Localization.Language` setting or browser); keys are English.
  New keys go into `src/Coworkee.Localization/Localization/*.json` for all 22 shipped languages; most UI words reuse existing keys.
  AirWorks' own chrome uses its `en`/`de` locale (`setLocale`), other languages fall back to English there.

### About dialog
- `AboutCredit` gets `IconBackground` (white logos such as AirWorks' need a backdrop).
- Nextended assemblies carry no version: the library records the Nextended package version at build time (`AssemblyMetadata("NextendedVersion")`)
  and `AboutVersion.Of` falls back to it for Nextended **[call]**.
- The credit tiles no longer print their URL; the tile stays a link.
- `AboutCredit.AirWorks(string? version)` returns the AirWorks entry (logo `https://fgilde.github.io/AirWorks/icons/logo-air.png` on #35496b).

## Testing
- `@coworkee/client`/`@coworkee/airworks`: `node --test` for the pure parts (BFF session mapping, palette mapping, logo address, OData query, settings value mapping, texts fallback).
- Client.Blazor: AboutTests (Nextended version, no URL text, AirWorks credit with backdrop).
- Bff: cookie name option. Aspire: `AddAirWorks` registers the client and BFF settings (existing Aspire test style).
- Cli: option/flag/InfoUrl, template content with and without `--airworks`, generated app with `--airworks` builds (npm included). Studio: card link and summary.
- Manual/HTTP: generated app in Aspire – AirWorks host healthy, `/bff/user` anonymous, `/bff/login` redirects to the auth server with client `{app}-airworks`, `/api/v1/themes/current` through the host.

## Out of scope / follow-ups
Publishing the npm packages; realtime (SignalR) in the AirWorks client; chat, reports, AI assistant, admin apps; AirWorks desktop profile on the server
(stays in localStorage); vite dev server with HMR inside Aspire; AirWorks chrome texts beyond en/de.

## Status (end of the session)

Done: everything of phase 1 and phase 2 above (apps profile, settings, files + MudEx preview, data, entityApp, notifications, API docs), About fixes, CLI/Studio option with info link, Aspire `AddAirWorks`, BFF cookie name, packages with tests, template with build, docs EN/DE.

Changed on the way **[call]**: the AirWorks desktop profile stays in localStorage (no server setting); the data app reads OData directly instead of the client-entity cache of the Blazor app; files dialogs use the browser's prompt/confirm (shortcut).

Follow-ups: see Out of scope, plus a browser test of the desktop (no browser automation was available for this work).
