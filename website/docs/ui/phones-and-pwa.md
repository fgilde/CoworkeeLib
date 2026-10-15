# Phones, installable app and push

The Blazor shell works on a phone the same way it works on a desk, and the app installs like a native one. Users can switch on push notifications per device.

## On a phone

Below 600 px width the layout changes by itself; nothing to configure:

- **App bar**: language, theme, right to left, colour mode and about move into one *More* menu. The user menu shows only the avatar.
- **Navigation**: an overlay that starts closed and closes after each click. The pinned mini strip of the desktop is only used on wider screens.
- **Data tables**: rows become cards. The empty "drag a column here" zone is hidden (dragging needs a mouse anyway), the search field takes the full width and facet menus open as a sheet from the bottom. On wider screens the column with the row actions stays visible while a wide table scrolls sideways.
- **Edit forms and side sheets** fill the screen, small dialogs keep a margin. Forms already use one column on narrow screens.
- **Page headers** put their buttons below the title.
- **Assistant**: the window covers the whole screen.
- **Touch**: on touch screens icon buttons, menu entries and navigation links are at least 44 px high.
- **Notched phones**: app bar, content, navigation and sheets keep clear of the safe areas. The template's `App.razor` has `viewport-fit=cover` in its viewport tag; add it to apps made before:

```html
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
```

The sign-in pages of the auth server do the same: on a phone the form fills the screen, inputs use 16 px text so iOS does not zoom in, and links and the *Stay signed in* box are easy to hit.

Your own pages get the rules too, as long as they use `PageHeader` and `CoworkeeDataTable`. For your own components, `IBrowserViewportService` of MudBlazor tells you the breakpoint.

## Installable app

One line in the head of `MyApp.Web/App.razor` turns the app into a Progressive Web App:

```razor title="MyApp.Web/App.razor"
<link rel="icon" href="coworkee-icon.svg" />
<Coworkee.Bff.Pwa.CoworkeePwa />
```

The component links the web manifest and colours the browser's title bar like the app bar of the visitor's theme. `Coworkee.Bff` serves the rest:

| Address | What it is |
|---|---|
| `/manifest.webmanifest` | Name of the app, colours and logo of the organisation's theme (the theme's logo, else the app's icon). |
| `/service-worker.js` | The service worker; its version follows the app's static files, so every deployment brings a new one. |
| `/bff/pwa/icon` | The icon: the theme's logo or the configured icon. |
| `/bff/pwa/offline` | The page shown while there is no network, in the theme and the user's language. |

Name and icon come from the configuration; Aspire sets both from `DisplayName` and `LogoUrl`:

```json title="appsettings.json of the web app"
"Coworkee": { "Bff": { "Pwa": { "Name": "My Shop", "Icon": "/coworkee-icon.svg" } } }
```

What the service worker does:

- It caches **static files only** (`_framework`, `_content`, styles, scripts, fonts, images), always asking the network first. The cached copy is only used without network, so a new version never mixes with old files.
- Pages without network get the offline page with a reload link.
- It never touches sign-in, `/bff`, `/api`, `/odata`, `/hubs`, the job dashboard or any request that is not a GET. The session cookie, the `X-CSRF` header and antiforgery work as before.
- A new version waits until the user agrees: the app shows *A new version of the app is available* with a *Reload* button.
- When the browser offers the installation, the app shows a hint once per browser, and the user menu keeps an *Install app* entry.

The app registers the worker only on pages that link a manifest, so without the component nothing changes. The template has the option `pwa` (on by default, `coworkee new --pwa false` leaves it out).

## Push notifications

With the Notifications module, every notification can also reach the user's phone or desktop as a push message, even when the app is closed. A click on the message opens the notification's link in the app.

In development one line in the app host is enough. It makes a VAPID key pair at the first start and keeps it in the user secrets of the app host (`Parameters:{name}-push-public-key` and `-push-private-key`):

```csharp title="MyApp.AppHost/AppHost.cs"
var app = builder.AddCoworkeeApp("myapp", options =>
{
    options.UsePush();
});
```

The template adds it when `pwa` and `notifications` are on. Without Aspire, or in production, give every service with the Notifications module the same pair:

```json
"Coworkee": {
  "Notifications": {
    "Push": { "PublicKey": "<base64url>", "PrivateKey": "<base64url>", "Subject": "mailto:ops@example.com" }
  }
}
```

The public key is the uncompressed P-256 point, the private key the 32 byte scalar, both base64url. `npx web-push generate-vapid-keys` prints a pair in this format. Keep the pair: a new one silences every subscribed device until its user switches push on again. Without `Subject` the push services get `PublicAppUrl` as contact. The pair is hidden on the *App settings* page.

Users switch push on and off per device on the notifications page (also the *Notifications* tab of the account page). The card lists their devices and removes old ones. Without keys the card stays hidden.

The API behind it:

| Endpoint | |
|---|---|
| `GET /api/v1/notifications/push` | Public key (null without keys) and the user's devices. |
| `PUT /api/v1/notifications/push` | Saves the browser's subscription (`endpoint`, `p256dh`, `auth`, `device`). The same browser signed in as another user moves to that user. |
| `DELETE /api/v1/notifications/push/{id}` | Removes a device of the user. |

Only `https` addresses of push services on the internet are accepted as endpoint: no IP addresses, no ports, no local host names.

Delivery runs as background job after the notification is saved. `INotifier` raises `NotificationCreated` through the outbox, `PushDeliveryJob` sends title and body in the user's language with the link of the notification. Devices that the push service no longer knows (404 or 410) are removed. The library encrypts the payload (RFC 8291) and signs the VAPID token (RFC 8292) with .NET's own cryptography, no extra package. To send through a service of your own, register your `IPushSender`:

```csharp
builder.Services.AddSingleton<IPushSender, MyPushSender>();
```

!!! note "iPhone and iPad"
    Safari delivers push only to an app that was added to the home screen (*Share*, then *Add to Home Screen*), from iOS 16.4 on.
