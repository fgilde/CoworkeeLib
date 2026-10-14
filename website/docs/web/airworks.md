# AirWorks client

A Coworkee app can have a second client next to the Blazor app: a web desktop built on [AirWorks](https://fgilde.github.io/AirWorks/), with windows, a taskbar, several desktops, folders, search and pop-outs. It signs in through the same auth server (and therefore the same Keycloak realm), talks to the same API, shows the logo and theme of the app and brings apps for settings, profile, files, data and notifications. The library ships the apps as npm packages; the app composes them and adds its own.

## Create an app with it

```bash
coworkee new Shop --airworks
```

In Coworkee Studio it is the card *AirWorks client* under *General*, with a link to the AirWorks documentation. The solution gets the project `Shop.AirWorks`, and the app host one line:

```csharp
app.AddWeb<Projects.Shop_Web>();
app.AddAirWorks<Projects.Shop_AirWorks>();
```

Start the app host as usual and open *shop-airworks* from the Aspire dashboard. Building the project needs Node.js 22 or newer: `dotnet build` runs `npm install` and `npm run build` in `ClientApp` (skip it with `-p:SkipClientBuild=true`).

## How it signs in

`Shop.AirWorks` is a small ASP.NET Core host with [Coworkee.Bff](../security/clients.md), exactly like the web host of the Blazor app:

- `AddAirWorks` registers it as a second client of the auth server (`shop-airworks`, redirect `/signin-oidc`, scope `shop_api`) and points its BFF at the first API.
- Signing in goes to the auth server, which offers its own form and Keycloak as the [sign-in mode](../security/authentication.md) says. The users, roles and permissions are those of the Blazor app.
- The tokens stay on the server. The browser holds a session cookie named `shop-airworks` (browsers send cookies to every port of `localhost`, so the two clients need different names) and sends `X-CSRF: 1` with every change.
- `/api`, `/odata`, `/hubs`, `/openapi` and `/swagger` are forwarded to the API with the access token.

## The client app

`ClientApp/src/main.ts` belongs to the app:

```ts
import { defaultApps, entityApp, useCoworkee } from '@coworkee/airworks';
import { AirWorks } from '@fgilde/airworks';
import '@fgilde/airworks/style.css';
import '@coworkee/airworks/style.css';
import logo from '../../../Shop.Web.Client/wwwroot/coworkee-icon.svg?url';

const coworkee = await useCoworkee(AirWorks, { title: 'Shop', logo });
await coworkee.register(
  ...defaultApps,
  entityApp({ entitySet: 'Products', icon: 'ratio-product.png', requiredPermissions: ['Catalog.Products.View'] }),
);
document.body.append(document.createElement('air-desktop'));
```

`useCoworkee` reads the user from the BFF, the theme from `/api/v1/themes/current` (colors of the user's color mode as CSS variables `--cw-primary`, `--cw-surface`, `--cw-text`, ..., the primary color as AirWorks' system color, the theme's logo or the one passed), the client settings and the texts of the user's language, registers the sign-in and the permissions (`/api/v1/identity/permissions/me`) with AirWorks and configures it. Options of AirWorks itself (wallpapers, `initialShortcuts`, `initialApps`, ...) go to `airWorks: { ... }`.

| App | Id | Shown when |
|---|---|---|
| Profile: name, phone, account pages, sign out | `coworkee.profile` | always |
| Settings: the user's, the organisation's and the system's, built from the setting definitions | `coworkee.settings` | always; organisation and system with `Settings.Manage` |
| Files: folders, upload (also by dropping files), rename, delete, download | `coworkee.files` | `Files.View` |
| File preview with the MudEx file display (pdf, images, office, archives, markdown, audio, video) | `coworkee.file-preview` | opened from Files |
| Data: every OData entity set as a paged, sortable and searchable table | `coworkee.data` | the app has OData |
| Notifications: read, mark read, delete | `coworkee.notifications` | the app has the Notifications module |
| API documentation (Swagger UI) | `coworkee.api-docs` | `ApiDocs.View` |
| `entityApp({ entitySet, ... })`: one entity set as an app of its own | `coworkee.entity.{set}` | its `requiredPermissions` |

Leave apps out by registering only those you want, or replace one by registering your own app with the same id after `defaultApps`. Your own apps are plain AirWorks apps (see the [AirWorks documentation](https://fgilde.github.io/AirWorks/)); a factory gets the `coworkee` object with the typed API client, `request`, the translation function `t`, the user, the theme and `notify`:

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

The file preview loads the [MudBlazor.Extensions web components](https://www.mudex.org) on first use from jsDelivr; pass `mudExUrl` to `useCoworkee` to host the bundle yourself.

## The packages

| Package | Content |
|---|---|
| `@coworkee/client` | Types of the library API generated from its OpenAPI description, `createCoworkeeClient()` (openapi-fetch with the session cookie and the CSRF header, or a bearer token), `apiRequest`, `ApiError` with the problem details, BFF helpers, theme, texts and OData helpers. Framework free, see [SDKs](sdk.md). |
| `@coworkee/airworks` | `useCoworkee`, the apps above, `renderEntityTable`, `loadMudEx`, the styles. Needs `@fgilde/airworks`. |

Their sources live in `typescript/` of the library. Until they are published on npm, the template ships them as tarballs in `ClientApp/vendor`; AirWorks itself comes from GitHub at a fixed commit.

## Limits

Chat, reports, the AI assistant and the administration pages (users, roles, themes, languages) stay in the Blazor app. The AirWorks desktop (shortcuts, open windows, wallpaper) is kept in the browser's local storage, and the client does not use realtime yet.
