# Sign-in pages and designs

The account pages of the auth server (sign-in, registration, two-step verification, forgotten and new password, consent, sign-out, the app launcher and the account security pages) follow the app's theme: the default theme of the system organisation gives them colors for light and dark mode, the font, the border radius and the logo, the configuration the app name. The color mode setting (`Theme.Mode`: system, light or dark, the user's or the installation's) applies as well.

## Layouts

Three layouts ship with the library:

| Layout | Look |
|---|---|
| `Card` | a centered card |
| `Split` | full page: image or video on one side, the form on the other |
| `Slide` (default) | a card with a colored panel next to the form. On the sign-in page the panel invites to create an account (when registration is on); on the registration page it moves to the other side and links back to sign-in. Browsers with cross-document view transitions animate the move. |

The pages work without script. A small script adds what a browser alone cannot: a button that shows the password, a hint while Caps Lock is on, a *Last used* badge on the sign-in method chosen the last time (only when there are external providers) and one submit per form. Passkeys need a second small script, `passkey.js`; without it their button stays hidden (see [Passkeys](passkeys.md)). Animations stop and videos stand still when the system asks for reduced motion.

**Page background.** Independent of the layout's image or video, `Design:BackgroundUrl` puts an image or video behind the whole page (cover, fixed while the page scrolls), optionally darkened with `Design:BackgroundDim`. `Card` and `Slide` show it around their card; `Split` shows it behind the form side, where the form then sits on a card of its own so it stays readable; the media side keeps its image or video. On phones `Card` and `Slide` fill the screen, so the background stays out of sight there. Own layouts get it as well, since `_Layout.cshtml` renders it. Without the value the pages look as before.

## Configuration

The design applies to the whole installation, whichever theme is the default. The configuration sets the starting point:

```json
"Coworkee": {
  "Auth": {
    "Design": {
      "Layout": "Split",
      "MediaUrl": "/media/welcome.mp4",
      "MediaSide": "Right",
      "BackgroundUrl": "/media/sky.jpg",
      "BackgroundDim": 30,
      "ShowAppName": true,
      "Texts": {
        "Title": "Welcome",
        "Titles": { "de": "Willkommen" },
        "Notice": "Maintenance on Sunday from 8 to 10 pm."
      }
    },
    "AllowDesignChanges": true
  }
}
```

| Option | Meaning |
|---|---|
| `Design:Layout` | `Card`, `Split`, `Slide` or the name of an own layout; unknown names fall back to `Slide` |
| `Design:MediaUrl` | an image, or a video for `.mp4`, `.webm`, `.ogg`, `.ogv` and `.mov`; a path on the auth server or an `http(s)` address, whose origin enters `img-src` and `media-src` of the content security policy |
| `Design:MediaSide` | `Left` (default) or `Right`; with `Slide` the panel of the registration page sits on the other side |
| `Design:BackgroundUrl` | an image or video behind the whole page in every layout, same rules as `MediaUrl` (path or `http(s)` address, its origin enters the policy); empty keeps the theme's background |
| `Design:BackgroundDim` | darkens the background, `0` to `90` percent (default `0`) |
| `Design:LoadingScreen` | background of the app's [loading screen](../modules/theming.md#loading-screen): `Animated`, `SignInMedia` (the image or video above) or `Plain` |
| `Design:ShowAppName` | `true` (default) shows the app name next to the logo and on the media panel of `Split` and `Slide`; `false` leaves the logo alone |
| `Design:Texts:Title`, `Subtitle`, `Notice` | own heading, a line under it and a notice above the form of the sign-in page, for every language; empty keeps the built-in heading *Sign in* and shows neither line nor notice |
| `Design:Texts:Titles`, `Subtitles`, `Notices` | the same per language (`"de"`, `"de-AT"`): the visitor's language (`de-AT`, then `de`) wins over the text for every language |
| `AllowDesignChanges` | `true` (default) lets administrators change the design on the page *Sign-in*; `false` keeps the configuration. Set it for the service that hosts the API, where the change is saved. |

The Aspire app host sets the same values with `options.SignIn` and serves a media path from the web app, see [Aspire app host](../hosting/aspire.md).

## Texts

The texts change the sign-in page only (`/Account/Login`); the other account pages keep their headings. They are plain text: the page encodes them, so markup shows as typed and cannot inject anything, and a line break in the notice stays one. The language is the one the auth server picks for the request (the browser's language or `?ui-culture=de`). The Keycloak login theme gets the texts too: its script sets them as text on Keycloak's sign-in page in the language of that page; without the app name the theme leaves Keycloak's header text empty. A stored design without the new values looks as before.

## On the page Sign-in

*Administration > System > Sign-in* (`/cw/admin/sign-in`, page key `sign-in`) gathers the whole sign-in and registration configuration. It is one more view on the same sources, with the forms of the settings page; nothing is stored differently:

| Part | Source |
|---|---|
| *Sign-in, security and registration* | the setting groups *Sign-in security* (sign-in methods, passkeys, sessions per user and the limit mode, password rules, lockout, password expiry and history, act within one role) and *Accounts* (self registration, activation, email confirmation), the same form as under *Settings*. *Sign-in methods* shows only while an external provider is configured; when the operator fixes the mode with `Coworkee:Auth:External:ModeOverride` it shows read-only with a hint (set the variable for the API too, which serves the settings) |
| *Registration form* | the part `Registration` of the app settings (address, allowed emails, documents and their slots), the form of the app settings with the other parts left out and saved unchanged |
| card *Sign-in page* | the design described below |
| *Texts of the sign-in page* | `ShowAppName` and `Texts` of the same section `Coworkee:Auth:Design`, in the form of the app configuration |

The page needs `Settings.Manage`; the menu shows it to the system organisation. Other organisations get only their own session settings there and no app configuration. The page follows changes of others at once: a saved setting reloads the settings form (unless it holds unsaved changes), a saved configuration value reloads the form of its section. *Administration > Themes* links to the page.

The card *Sign-in page* shows a thumbnail per layout to pick from, the image or video address and its side, the page background with its darkening, and the background of the app's [loading screen](../modules/theming.md#loading-screen). The folder buttons in the address fields pick the image or video from the app's files; a private file is made public first (after a question), and the field gets its absolute public address, which the auth server, Keycloak and the loading screen can load without sign-in. *Save* stores the values as the app configuration section `Coworkee:Auth:Design` (table `cw.ConfigurationEntries`, see [App configuration](../modules/settings.md)); *Restore defaults* drops them, so the configured values apply again.

*Preview* opens the real sign-in page of the auth server with the values the card shows now, saved or not, in a full screen dialog. The dialog frames `/bff/sign-in-preview?design={json}`, which sends the frame on to the auth server's `/Account/Preview`; that address renders the sign-in page with the given design after the same checks as a saved one. It answers only a `GET` that the browser marks as frame load (`Sec-Fetch-Dest: iframe`), and only the origins of registered clients may frame it (`frame-ancestors`), so a link or another site cannot show a crafted design under the auth server's address. The frame runs without forms (`sandbox`), nobody signs in there, and the admin's own session stays as it is. The web app's policy allows the auth server's origin in `frame-src` for this. With Keycloak as sign-in the preview shows the Coworkee auth server's page, which Keycloak does not have; there the Keycloak card applies the design.

The card shows for administrators of the system organisation who may manage settings (`Settings.Manage`), because the section applies to every organisation. It stays hidden for everyone else and while `AllowDesignChanges` is off.

**How fast the auth server follows.** The auth server reads the design from its configuration on every request. It sees a change once its database configuration source reloads: `AddCoworkeeDatabaseConfiguration` checks every 15 seconds by default (the auth host of the template adds it). A host in the same process as the API sees the change at once. Colors, font and logo come from the default theme and are cached for one minute.

The client options control the card:

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.AllowSignInDesign = false;        // hides the card
    options.SignInLayouts.Add("Aurora");      // offers an own layout
});
```

An own layout gets a generic thumbnail with its name. Themes saved with the former per theme value (`SignIn` in the theme options) are ignored; set the design once on the page *Sign-in*.

## Own designs

The layouts are Razor layouts next to the pages of the library: `Pages/_DesignCard.cshtml`, `Pages/_DesignSplit.cshtml` and `Pages/_DesignSlide.cshtml`, all inside `Pages/_Layout.cshtml`, which renders the head, the theme variables, the style sheet and the script. As with every Razor class library, a view of the auth host at the same path wins:

- **Own layout**: add `Pages/_DesignAurora.cshtml` to the auth host and set `Design:Layout` to `Aurora` (or choose it on the page *Sign-in*).
- **Replace a built-in one**: add `Pages/_DesignCard.cshtml` to the auth host.
- **Replace everything**: add `Pages/_Layout.cshtml`; then the style sheet and the script are yours as well.

```cshtml
@* Pages/_DesignAurora.cshtml in the auth host *@
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@inject Coworkee.AuthServer.AuthBrandingProvider Branding
@{
  Layout = "_Layout";
  ViewData["Design"] = "Aurora";
  var brand = await Branding.GetAsync(Context.RequestAborted);
}
<div class="aurora">
  <main class="panel">
    <partial name="_Brand" model="brand" />
    @RenderBody()
  </main>
</div>
<style>
  body.layout-aurora { display: grid; place-items: center; background: radial-gradient(circle at top, var(--signal), var(--paper) 60%); }
  .aurora .panel { padding: 40px; background: var(--surface); border-radius: calc(var(--radius) * 3); }
</style>
```

`AuthBranding` carries the app name, the logo, the theme's CSS and the resolved `Design`; the partial `_Media` renders its image or video. The page model is `ViewContext.ViewData.Model` (`LoginModel`, `RegisterModel`, ...), so a layout can tell the pages apart. The style sheet defines these variables, which the theme fills: `--paper`, `--surface`, `--ink`, `--ink-2`, `--rule`, `--signal`, `--on-signal`, `--accent`, `--error`, `--success`, `--radius` and `--font`. The content security policy allows inline styles, but scripts only from the auth server itself.
