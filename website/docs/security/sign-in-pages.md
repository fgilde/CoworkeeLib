# Sign-in pages and designs

The account pages of the auth server (sign-in, registration, two-step verification, forgotten and new password, consent, sign-out, the app launcher and the account security pages) follow the app's theme: the theme the browser used last, else the default theme of the system tenant, gives them colors for light and dark mode, the font, the border radius, the logo and the sign-in design it names (see [Designs per theme](#designs-per-theme)); the configuration gives the app name. The color mode setting (`Theme.Mode`: system, light or dark, the user's or the installation's) applies as well.

## Layouts

Seven layouts ship with the library, each with a look of its own:

| Layout | Look |
|---|---|
| `Card` | a centered card. *Surface* `Glass` makes it frosted glass (translucent, blurred, a fine border and shadow; without a background over a soft gradient of the theme's primary and tertiary color; browsers without `backdrop-filter` get the plain surface). *Animation* `Aurora` lets soft fields of the page fill's colors (else the theme's primary, secondary and tertiary color) drift behind it. |
| `Split` | full page: image, video or fill on one side, the form on the other |
| `Slide` (default) | a card with a colored panel next to the form. On the sign-in page the panel invites to create an account (when registration is on); on the registration page it moves to the other side and links back to sign-in. Browsers with cross-document view transitions animate the move. |
| `Spotlight` | the page background fills the whole screen, the app name stands on it in big letters, and the form floats in a full-height panel at the left or right edge (24 px inset, rounded, solid or glass) that slides in. On phones the panel becomes a bottom sheet under the open background. |
| `Poster` | two columns without a card: the poster side carries the app name in huge letters, the sign-in title and subtitle of the default design as claim and, optionally, an image or video; the form side stays calm with line inputs and round buttons. |
| `Stack` | the form card in front of two tilted cards (the second one offset) that show the panel fill or the image; between sign-in and registration the stack flips (view transition) and the cards lean the other way. *Tilt* sets the angle, 0 to 12 degrees (default 6). |
| `Minimal` | no card, no border: a large logo (*Logo size* small, medium or large) and the form right on the page background; over an image or video the form gets a soft frosted backdrop so it stays readable. |

All seven work in light and dark mode, with registration, passkeys, external providers, the texts and without the app name. On phones `Card` and `Slide` fill the screen like an app; a card over a background, glass or aurora keeps floating with a small margin, since the background is the point. Every entrance animation, the drifting fields, the shifting gradients and the flip stop when the system asks for reduced motion.

The pages work without script. A small script adds what a browser alone cannot: a button that shows the password, a hint while Caps Lock is on, a *Last used* badge on the sign-in method chosen the last time (only when there are external providers) and one submit per form. Passkeys need a second small script, `passkey.js`; without it their button stays hidden (see [Passkeys](passkeys.md)). Animations stop and videos stand still when the system asks for reduced motion.

### Options per layout

Each layout shows only the options that make sense for it, the same in the admin card, the Studio and here (`SignInLayoutOptions.For(layout)` in `Coworkee.Contracts.Theming`):

| Layout | Background 1 | Background 2 | Side | Surface | Animation | Darken | Tilt | Logo size |
|---|---|---|---|---|---|---|---|---|
| `Card` | page background | - | - | yes | yes | yes | - | - |
| `Split` | form side | media side | image side | - | - | - | - | - |
| `Slide` | page background | panel | panel side | - | - | yes | - | - |
| `Spotlight` | page background | - | panel side | yes | yes | yes | - | - |
| `Poster` | form side | poster | poster side | - | - | - | - | - |
| `Stack` | page background | cards behind | - | - | yes | yes | yes | - |
| `Minimal` | page background | - | - | - | yes | yes | - | yes |
| own layout | page background | panel | image side | yes | yes | yes | - | - |

The pages leave out what a layout does not show, so a value stored for another layout does no harm.

### Backgrounds: files, colors and gradients

Every background is one of four things: the theme's (nothing set), an image or video (background 1: `BackgroundUrl`, background 2: `MediaUrl`), a color or a gradient (`PageFill`, `PanelFill`, used when the file of the same background is empty). A fill is either `Color` or `Gradient`:

```json
"PageFill": { "Color": "#0f2027" },
"PanelFill": {
  "Gradient": {
    "Kind": "Linear",
    "Angle": 135,
    "Animated": true,
    "Stops": [
      { "Color": "primary", "Position": 0 },
      { "Color": "#ffc371", "Position": 100 }
    ]
  }
}
```

- `Kind`: `Linear`, `Radial` or `Conic`; `Angle` 0 to 359 degrees (direction of a linear gradient, start of a conic one); `Animated` shifts the gradient slowly.
- Up to 8 stops with `Position` 0 to 100.
- Colors: `#rgb`, `#rrggbb`, `#rrggbbaa`, `rgb()`, `rgba()`, `hsl()` or `hsla()` with numbers only, or a theme color `primary`, `secondary`, `tertiary`, `background` or `surface`, which follows the theme (light, dark, another theme).

Fills end up in the page's styles, so the auth server, the preview and the Keycloak theme rebuild them from checked parts only (`SignInFills`): any other color is dropped, numbers are clamped, extra stops ignored, a gradient with one valid stop becomes that color. Text that sits right on a fill (the minimal form, the poster's form side, the spotlight caption, panels) turns light or dark with the brightness of the fill's colors.

On the page *Sign-in* each background has a segmented choice *Theme default*, *Image or video*, *Color* and *Gradient*. The gradient control shows a large live preview and the stops as handles on a track: a click on the track adds a stop with the color at that point, a handle drags (or moves with the arrow keys, Shift for steps of 10), *Delete* removes it (two stay); the selected stop has a color field, the theme colors and its position; kind, angle (with a dial), *Animated* and a few presets complete it.

**Former layouts Glass and Aurora.** They are options of `Card` now: a stored design with `Layout` `Glass` reads as `Card` with `Surface` `Glass` (its image or video becomes the page background when it has none), `Aurora` as `Card` with `Animation` `Aurora`; the auth server, the preview, the Keycloak theme and the admin card all read it that way, and saving the page stores the new form. The constants `SignInLayouts.Glass` and `SignInLayouts.Aurora` remain, marked obsolete. An own view `Pages/_DesignGlass.cshtml` or `_DesignAurora.cshtml` of the auth host still wins.

**Page background.** Independent of background 2, `Design:BackgroundUrl` (or `PageFill`) puts an image, video or fill behind the whole page (cover, fixed while the page scrolls), optionally darkened with `Design:BackgroundDim` where the layout offers it. `Card`, `Slide`, `Stack` and `Spotlight` show it around their form; `Split` and `Poster` show it behind the form side (`Split` then puts the form on a card of its own so it stays readable). On phones `Card` and `Slide` without a background fill the screen. Own layouts get it as well, since `_Layout.cshtml` renders it. Without the value the pages look as before.

## Configuration

The design applies to the whole installation unless a theme names one of the named designs (see [Designs per theme](#designs-per-theme)). The configuration sets the starting point:

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
| `Design:Layout` | `Card`, `Split`, `Slide`, `Spotlight`, `Poster`, `Stack`, `Minimal` or the name of an own layout; unknown names fall back to `Slide`, the former `Glass` and `Aurora` read as `Card` (see above) |
| `Design:MediaUrl` | an image, or a video for `.mp4`, `.webm`, `.ogg`, `.ogv` and `.mov`; a path on the auth server or an `http(s)` address, whose origin enters `img-src` and `media-src` of the content security policy |
| `Design:MediaSide` | `Left` or `Right`: the image side of `Split` and the poster side of `Poster` (default left), the panel side of `Slide` and `Spotlight` (default right); with `Slide` the panel of the registration page sits on the other side |
| `Design:BackgroundUrl` | an image or video behind the whole page in every layout, same rules as `MediaUrl` (path or `http(s)` address, its origin enters the policy); empty keeps the theme's background |
| `Design:BackgroundDim` | darkens background 1, `0` to `90` percent (default `0`), in the layouts that offer it |
| `Design:PageFill`, `Design:PanelFill` | background 1 and 2 as color or gradient, used when `BackgroundUrl` or `MediaUrl` is empty; see [Backgrounds](#backgrounds-files-colors-and-gradients) |
| `Design:Surface` | `Solid` (default) or `Glass` for `Card` and `Spotlight` |
| `Design:Animation` | `None` (default) or `Aurora`: drifting color fields behind the form |
| `Design:Tilt` | tilt of the cards behind the form in `Stack`, `0` to `12` degrees (default `6`) |
| `Design:LogoSize` | `Small`, `Medium` (default) or `Large` in `Minimal` |
| `Design:LoadingScreen` | background of the app's [loading screen](../modules/theming.md#loading-screen): `Animated`, `SignInMedia` (the image or video above) or `Plain` |
| `Design:ShowAppName` | `true` (default) shows the app name next to the logo and on the media panel of `Split` and `Slide`; `false` leaves the logo alone |
| `Design:Texts:Title`, `Subtitle`, `Notice` | own heading, a line under it and a notice above the form of the sign-in page, for every language; empty keeps the built-in heading *Sign in* and shows neither line nor notice |
| `Design:Texts:Titles`, `Subtitles`, `Notices` | the same per language (`"de"`, `"de-AT"`): the visitor's language (`de-AT`, then `de`) wins over the text for every language |
| `Design:Designs` | further, named designs themes can choose: each with `Key`, `Name` and the look values `Layout`, `MediaUrl`, `MediaSide`, `BackgroundUrl`, `BackgroundDim`, `PageFill`, `PanelFill`, `Surface`, `Animation`, `Tilt`, `LogoSize`; see [Designs per theme](#designs-per-theme) |
| `AllowDesignChanges` | `true` (default) lets administrators change the design on the page *Sign-in*; `false` keeps the configuration. Set it for the service that hosts the API, where the change is saved. |

The Aspire app host sets the same values with `options.SignIn` and serves a media path from the web app, see [Aspire app host](../hosting/aspire.md).

## Designs per theme

The values above are the **default design**. `Design:Designs` adds named designs, and a theme names the one it wants: in the theme editor (*Sign-in design*, default *Default design*), or in the theme's options as `SignInDesign` with the design's key. A named design brings its own look (layout, both backgrounds, side and the layout's options); the texts, *Show the app name* and the loading screen stay those of the default design, so a notice reaches everyone whichever theme they use.

```json
"Coworkee": {
  "Auth": {
    "Design": {
      "Layout": "Slide",
      "Designs": [
        { "Key": "frost", "Name": "Frost", "Layout": "Card", "Surface": "Glass", "BackgroundUrl": "/media/sea.mp4" },
        { "Key": "night", "Name": "Night", "Layout": "Spotlight", "PageFill": { "Gradient": { "Kind": "Radial", "Stops": [ { "Color": "#845ec2", "Position": 0 }, { "Color": "#1b1035", "Position": 80 } ] } } }
      ]
    }
  }
}
```

```csharp
// a theme of the app that shows the design "night"
builder.Services.Configure<ThemingOptions>(o => o.Themes.Add(new AppTheme("Night", "Ocean", new Dictionary<string, string> { ["Primary"] = "#5e35b1" })
{
    Options = new Dictionary<string, object> { [SignInDesign.ThemeOption] = "night" },
}));
```

Which design a sign-in page shows:

1. the design the theme this browser used last names: the web app remembers the applied theme in its cookie `coworkee-loading` and names it in the authorization request (`coworkee_theme`); the auth server keeps it in its own cookie `coworkee-theme` (HttpOnly, SameSite Lax, one year) for every account page of that browser. Only a built-in or published theme of the system tenant counts; any other id, or none, falls through. That theme's colors, font and logo apply as well.
2. else the design the default theme of the system tenant names,
3. else the default design; also when a theme names a design that no longer exists.

The Keycloak sign-in page cannot know the browser's theme: it shows the design the default theme names. An existing configuration without `Designs` looks exactly as before; nothing needs to change.

## Texts

The texts change the sign-in page only (`/Account/Login`); the other account pages keep their headings. They are plain text: the page encodes them, so markup shows as typed and cannot inject anything, and a line break in the notice stays one. The language is the one the auth server picks for the request (the browser's language or `?ui-culture=de`). The Keycloak login theme gets the texts too: its script sets them as text on Keycloak's sign-in page in the language of that page; without the app name the theme leaves Keycloak's header text empty. A stored design without the new values looks as before.

## On the page Sign-in

*Administration > System > Sign-in* (`/cw/admin/sign-in`, page key `sign-in`) gathers the whole sign-in and registration configuration in tabs, one card per topic. It is one more view on the same sources; nothing is stored differently, and *Settings* keeps showing and editing every value:

| Tab | Controls | Source |
|---|---|---|
| *Methods* | *Password*, *External provider* or *Both* as a segmented choice; the switch *Passkeys* | settings `Auth.LoginMode`, `Account.Passkeys.Enabled` |
| *Security* | password rules (length and different characters in characters, expiry in days, history in passwords, four switches), lockout (attempts, minutes), sessions (sessions per user, what happens beyond the limit), *More settings* | the setting groups *Sign-in security* and *Accounts* |
| *Registration* | switches for self registration, activation, email confirmation and the address; allowed emails as chips; the documents as a list (name, hint, required, accepted file types, largest file, translations) | the settings `Account.AllowRegistration`, `Account.RegistrationRequiresActivation`, `Account.RegistrationRequiresEmailConfirmation` and the part `Registration` of the app settings |
| *Appearance* | the card *Sign-in designs* and below it the card of the selected design, both described below | the app configuration section `Coworkee:Auth:Design` |
| *Texts* | *Show the app name*; title, subtitle and notice for every language and, after picking a language, for that language (the text for every language shows as placeholder) | `ShowAppName` and `Texts` of the same section |

One save bar in the sticky tab bar saves all tabs: it shows *Unsaved changes* while something differs from what was loaded, *Discard* restores that, *Save* sends only what changed, in the same form the generic pages use: the changed settings in one call, the app settings with their part `Registration` replaced and all other parts as they came, the design as a whole. A failing part reports its error and keeps its changes; the others are saved.

The page arranges the settings it knows; any other setting of the two groups (an app's own, *Act within one role*) shows under *More settings* with a control of its type (switch, number, select, text): the component `SettingField`, which pages of an app can use as well. A setting the app does not define does not show: without an external provider there is no setting *Sign-in methods*, and *Methods* says that users sign in with their password. When the operator fixes the mode with `Coworkee:Auth:External:ModeOverride`, the fixed mode shows selected and read-only with a hint (set the variable for the API too, which serves the settings). Locked or hidden parts of the app settings show read-only (*Set in the configuration*) or not at all. The documents need a document store (the Files module or the app's own); while the app defines no file permission, the list is read-only with a hint.

The page needs `Settings.Manage`; the menu shows it to the system tenant. Other tenants get only the tab *Security* with their own session settings and no app configuration. The page follows changes of others at once: a saved setting or configuration value reloads its part, unless that part holds unsaved changes. *Administration > Themes* links to the page.

The card *Sign-in designs* shows the default design and the named ones as tiles: a thumbnail of the layout in the current theme's colors, the name, the layout and the themes that show it (for the default design: the themes naming none, or a design that is gone). *Add design* starts a named design (a glass card), *Duplicate* copies the look of the selected design under a new name and key, the field *Name* renames it (the key that themes store stays), *Delete* asks first and names the themes using the design, which then show the default design. The page follows theme changes of others at once. Like every other change on the page, these wait for the save bar.

The card below edits the selected design. It shows a thumbnail per layout to pick from (drawn in the design's fills), then only what the chosen layout offers (see [Options per layout](#options-per-layout)): its one or two backgrounds, each with *Theme default*, *Image or video*, *Color* and *Gradient*, the side, surface, aurora, darkening, tilt or logo size; for the default design also the background of the app's [loading screen](../modules/theming.md#loading-screen) (and its image or video when the layout has no second background). The folder buttons in the address fields pick the image or video from the app's files; a private file is made public first (after a question), and the field gets its absolute public address, which the auth server, Keycloak and the loading screen can load without sign-in. The save bar stores the values as the app configuration section `Coworkee:Auth:Design` (table `cw.ConfigurationEntries`, see [App configuration](../modules/settings.md)); *Restore defaults* drops them after a question, so the configured values apply again.

*Preview* opens the real sign-in page of the auth server with the values the card shows now, saved or not, in a full screen dialog; for a named design with the texts of the default design. The dialog frames `/bff/sign-in-preview?design={json}`, which sends the frame on to the auth server's `/Account/Preview`; that address renders the sign-in page with the given design after the same checks as a saved one. It answers only a `GET` that the browser marks as frame load (`Sec-Fetch-Dest: iframe`), and only the origins of registered clients may frame it (`frame-ancestors`), so a link or another site cannot show a crafted design under the auth server's address. The frame runs without forms (`sandbox`), nobody signs in there, and the admin's own session stays as it is. The web app's policy allows the auth server's origin in `frame-src` for this. With Keycloak as sign-in the preview shows the Coworkee auth server's page, which Keycloak does not have; there the Keycloak card applies the design.

The card shows for administrators of the system tenant who may manage settings (`Settings.Manage`), because the section applies to every tenant. It stays hidden for everyone else and while `AllowDesignChanges` is off.

**How fast the auth server follows.** The auth server reads the design from its configuration on every request. It sees a change once its database configuration source reloads: `AddCoworkeeDatabaseConfiguration` checks every 15 seconds by default (the auth host of the template adds it). A host in the same process as the API sees the change at once. Colors, font, logo and the design key of the themes the pages may show are cached for one minute.

The client options control the card:

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.AllowSignInDesign = false;        // hides the card
    options.SignInLayouts.Add("Ribbon");      // offers an own layout
});
```

An own layout gets a generic thumbnail with its name. Themes saved with the former per theme value (`SignIn` in the theme options) are ignored; give them a named design instead.

## Own designs

The layouts are Razor layouts next to the pages of the library: `Pages/_DesignCard.cshtml`, `Pages/_DesignSplit.cshtml`, `Pages/_DesignSlide.cshtml`, `Pages/_DesignSpotlight.cshtml`, `Pages/_DesignPoster.cshtml`, `Pages/_DesignStack.cshtml` and `Pages/_DesignMinimal.cshtml`, all inside `Pages/_Layout.cshtml`, which renders the head, the theme variables, the style sheet and the script. As with every Razor class library, a view of the auth host at the same path wins:

- **Own layout**: add `Pages/_DesignRibbon.cshtml` to the auth host and set `Design:Layout` to `Ribbon` (or choose it on the page *Sign-in*).
- **Replace a built-in one**: add `Pages/_DesignCard.cshtml` to the auth host.
- **Replace everything**: add `Pages/_Layout.cshtml`; then the style sheet and the script are yours as well.

```cshtml
@* Pages/_DesignRibbon.cshtml in the auth host *@
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@inject Coworkee.AuthServer.AuthBrandingProvider Branding
@{
  Layout = "_Layout";
  ViewData["Design"] = "Ribbon";
  var brand = await Branding.GetAsync(Context.RequestAborted);
}
<div class="ribbon">
  <main class="panel">
    <partial name="_Brand" model="brand" />
    @RenderBody()
  </main>
</div>
<style>
  body.layout-ribbon { display: grid; place-items: center; background: radial-gradient(circle at top, var(--signal), var(--paper) 60%); }
  .ribbon .panel { padding: 40px; background: var(--surface); border-radius: calc(var(--radius) * 3); }
</style>
```

`AuthBranding` carries the app name, the logo, the theme's CSS and the resolved `Design`; the partial `_Media` renders its image or video. The page model is `ViewContext.ViewData.Model` (`LoginModel`, `RegisterModel`, ...), so a layout can tell the pages apart. The style sheet defines these variables, which the theme fills: `--paper`, `--surface`, `--ink`, `--ink-2`, `--rule`, `--signal`, `--on-signal`, `--accent`, `--tertiary`, `--error`, `--success`, `--radius` and `--font`; `_Layout.cshtml` adds the fills as `--page-fill` and `--panel-fill` on `<body>` (with the classes `has-background`, `page-dark`/`page-light`, `panel-dark`/`panel-light`), `AuthBranding.Options` says what the layout shows. The content security policy allows inline styles, but scripts only from the auth server itself.
