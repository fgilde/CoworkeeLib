# AirWorks client

A Coworkee app can have a second client next to the Blazor app: a web desktop built on [AirWorks](https://fgilde.github.io/AirWorks/), with windows, a taskbar, several desktops, folders, search and pop-outs. It signs in through the same auth server (and therefore the same Keycloak realm), talks to the same API, shows the logo and theme of the app and brings nearly everything the Blazor app offers: settings, profile, files with preview, data tables, chat, reports, the AI assistant, notifications and the administration (users, groups, roles, applications, scopes, API keys, themes, languages, translations, mail templates, campaigns, recipient lists, mail log, mail and AI settings, backups, tenants, editions, audit log). The library ships the apps as npm packages; the app composes them and adds its own.

## Create an app with it

```bash
coworkee new Shop --airworks
```

In Coworkee Studio it is the card *AirWorks client* under *General*, with a link to the AirWorks documentation. The solution gets the project `Shop.AirWorks`, and the app host one line:

```csharp
app.AddWeb<Projects.Shop_Web>();
app.AddAirWorks<Projects.Shop_AirWorks>();
```

Start the app host as usual and open *shop-airworks* from the Aspire dashboard. Building the project needs Node.js 22.18 or newer: `dotnet build` runs `npm install` and `npm run build` in `ClientApp` (skip it with `-p:SkipClientBuild=true`).

## How it signs in

`Shop.AirWorks` is a small ASP.NET Core host with [Coworkee.Bff](../security/clients.md), exactly like the web host of the Blazor app:

- `AddAirWorks` registers it as a second client of the auth server (`shop-airworks`, redirect `/signin-oidc`, scope `shop_api`) and points its BFF at the first API.
- Signing in goes to the auth server, which offers its own form and Keycloak as the [sign-in mode](../security/authentication.md) says. The users, roles and permissions are those of the Blazor app.
- The tokens stay on the server. The browser holds a session cookie named `shop-airworks` (browsers send cookies to every port of `localhost`, so the two clients need different names) and sends `X-CSRF: 1` with every change.
- `/api`, `/odata`, `/hubs`, `/openapi` and `/swagger` are forwarded to the API with the access token, so the realtime hub works through the same session.

## The client app

`ClientApp/src/main.ts` belongs to the app:

```ts
import { adminApps, defaultApps, entityApp, useCoworkee } from '@coworkee/airworks';
import { AirWorks } from '@fgilde/airworks';
import '@fgilde/airworks/style.css';
import '@coworkee/airworks/style.css';
import logo from '../../../Shop.Web.Client/wwwroot/coworkee-icon.svg?url';

const coworkee = await useCoworkee(AirWorks, { title: 'Shop', logo, mudExUrl: '/mudex/mudex.js' });
await coworkee.register(
  ...defaultApps,
  ...adminApps,
  entityApp({ entitySet: 'Products', liveType: 'Product', icon: 'ratio-product.png', requiredPermissions: ['Catalog.Products.View'] }),
);
document.body.append(document.createElement('air-desktop'));
```

`useCoworkee` reads the user from the BFF, the theme from `/api/v1/themes/current` (colors of the user's color mode as CSS variables `--cw-primary`, `--cw-surface`, `--cw-text`, ..., the primary color as AirWorks' system color, the theme's logo or the one passed), the client settings and the texts of the user's language, registers the sign-in and the permissions (`/api/v1/identity/permissions/me`) with AirWorks and configures it. Options of AirWorks itself (wallpapers, `initialShortcuts`, `initialApps`, ...) go to `airWorks: { ... }`.

### Apps for every user (`defaultApps`)

| App | Id | Shown when |
|---|---|---|
| Profile: name, phone, account pages, sign out | `coworkee.profile` | always |
| Settings: the user's, the organisation's and the system's, built from the setting definitions | `coworkee.settings` | always; organisation and system with `Settings.Manage` |
| Files: folders, upload (also by dropping files), rename, delete, download | `coworkee.files` | `Files.View` |
| File preview with the MudEx file display (pdf, images, office, archives, markdown, audio, video) | `coworkee.file-preview` | opened from Files |
| Data: every OData entity set as a paged, sortable and searchable table | `coworkee.data` | the app has OData |
| Chat: people of the organisation with presence and unread counts, messages arrive live | `coworkee.chat` | `Chat.Use` and the Chat module |
| Reports: list, favourites, duplicate, delete; a report opens with its KPIs, charts and tables | `coworkee.reports`, `coworkee.report` | `Reports.View` and the Reporting module |
| Assistant: conversations, answers streamed as they arrive, confirmation of tools that change data | `coworkee.assistant` | `Ai.Chat` and the AI module |
| Notifications: read, mark read, delete; new ones also pop up as a toast | `coworkee.notifications` | the app has the Notifications module |
| API documentation (Swagger UI) | `coworkee.api-docs` | `ApiDocs.View` |
| `entityApp({ entitySet, ... })`: one entity set as an app of its own; `liveType` reloads it on changes | `coworkee.entity.{set}` | its `requiredPermissions` |

### Administration (`adminApps`)

The administration apps get no desktop shortcut of their own: the app *Administration* lists them grouped like the administration menu of the web app and is only shown when the user may open one of them. Each one needs the permission of its page in the Blazor app; apps marked *system* show only for users of the system organisation, like the host-only pages.

| Section | Apps (permission) |
|---|---|
| Identity | Users (`Identity.Users.View`; edit, roles, groups, permissions, password, lock, reset mail, sign out everywhere), Groups (`Identity.Groups.View`), Roles with their permissions (`Identity.Roles.View`), Applications and Scopes (`Identity.Clients.Manage`, system), API keys (`ApiKeys.Manage`) |
| System | Themes (`Themes.Manage`; new from a built-in theme, palettes, logo, CSS, default, Keycloak sign-in page), Backups, Services, Editions, Tenants with their features (system), AI settings with a connection test (system) |
| Communication | Mail templates with live preview and test mail (`Mail.Templates.Manage`), Mail campaigns with recipient preview, test, schedule and cancel, Recipient lists (`Mail.Campaigns.Send`), Mail log (`Mail.Log.View`), Mail settings with a connection test (`Settings.Manage`) |
| Localization | Languages (switch on/off, default), Translations with machine translation of the missing texts (`Localization.Manage`) |
| Monitoring | Audit log with the changed fields (`Audit.View`), AI tool calls (`Ai.Audit`) |

Leave apps out by registering only those you want, or replace one by registering your own app with the same id after `defaultApps`. Your own apps are plain AirWorks apps (see the [AirWorks documentation](https://fgilde.github.io/AirWorks/)); a factory gets the `coworkee` object with the typed API client, `request`, the realtime hub, the translation function `t`, the user, the theme and `notify`:

```ts
const ordersApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: 'shop.orders', title: coworkee.t('Orders'), icon: 'fn-planning.png', requiredPermissions: ['Orders.View'],
  render: async ({ host }) => {
    const orders = await coworkee.request<Order[]>('/api/v1/orders');
    host.innerHTML = `<div class="cw-app">${orders.length} orders</div>`;
  },
});
await coworkee.register(...defaultApps, ordersApp);
```

Lists with create, edit, delete and row actions are one call: `crudApp({ id, title, icon, permissions, load, columns, create, edit, remove, actions })` (the administration apps are built with it); `formDialog`, `confirmDialog` and `infoDialog` are the dialogs it uses.

### Realtime

`coworkee.realtime` connects to the realtime hub of the app on the first subscription (SignalR through the BFF) and subscribes every followed topic again after a reconnect:

```ts
const stop = coworkee.realtime.subscribe('type:Order', ({ type, payload }) => reload());
```

The desktop itself follows the user's topic (new notifications pop up, a session ended elsewhere signs out) and the permission topics (changed roles or grants show and hide apps at once). Chat and presence, the notifications app and `entityApp({ liveType })` follow their topics while their window is open.

### File preview

The file preview uses the [MudBlazor.Extensions web components](https://www.mudex.org). The template hosts the bundle itself: `mudex-webcomponents` is a dev dependency of `ClientApp`, the vite build copies it to `wwwroot/mudex`, and the host serves the `.dat` files of the .NET runtime. Without `mudExUrl`, `useCoworkee` loads it from unpkg.

## The packages

| Package | Content |
|---|---|
| `@coworkee/client` | Types of the library API generated from its OpenAPI description, `createCoworkeeClient()` (openapi-fetch with the session cookie and the CSRF header, or a bearer token), `apiRequest`, `ApiError` with the problem details, `createRealtime()` (SignalR), `postNdjson` for streamed answers, BFF helpers, theme, texts and OData helpers. Framework free, see [SDKs](sdk.md). |
| `@coworkee/airworks` | `useCoworkee`, the apps above, `crudApp`, the dialogs, `renderEntityTable`, `loadMudEx`, the styles. Needs `@fgilde/airworks`. |

Their sources live in `typescript/` of the library. Every release publishes them to npm in the version of the release: `a.b.c` stays, `a.b.c.d` becomes `a.b.(c*1000+d)` because npm knows no fourth part (release `0.0.1.21` is `0.0.1021` on npm). The generated `ClientApp/package.json` references exactly the version of the template it was made with. AirWorks itself comes from GitHub at a fixed commit.

### Local template builds

A template packed from a local checkout (`pwsh build/pack-local.ps1`) has no matching npm release. It ships the two packages as tarballs, and `coworkee new --template-source <nupkg>` makes the client use them (template parameter `npmPackages=vendor`, `file:vendor/*.tgz`). With `dotnet new coworkee` directly, pass `--npmPackages vendor` yourself. To switch an app to the published packages later, put the npm version into `ClientApp/package.json` and delete `ClientApp/vendor`.

## Limits

Reports are viewed, not designed: the report designer, the dashboard filters and the drill-down stay in the Blazor app. The AirWorks desktop (shortcuts, open windows, wallpaper) is kept in the browser's local storage. The MudEx bundle brings MudBlazor's page styles; `@coworkee/airworks/style.css` keeps the desktop's typography, other MudBlazor styles may still show in your own apps once a preview was open.
