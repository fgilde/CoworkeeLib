# Sign-in pages and designs

The account pages of the auth server (sign-in, registration, two-step verification, forgotten and new password, consent, sign-out, the app launcher and the account security pages) follow the app's theme: the default theme of the system organisation gives them colors for light and dark mode, the font, the border radius and the logo, the configuration the app name. The color mode setting (`Theme.Mode`: system, light or dark, the user's or the installation's) applies as well.

## Layouts

Three layouts ship with the library:

| Layout | Look |
|---|---|
| `Card` | a centered card |
| `Split` | full page: image or video on one side, the form on the other |
| `Slide` (default) | a card with a colored panel next to the form. On the sign-in page the panel invites to create an account (when registration is on); on the registration page it moves to the other side and links back to sign-in. Browsers with cross-document view transitions animate the move. |

The pages work without script. A small script adds what a browser alone cannot: a button that shows the password, a hint while Caps Lock is on, a *Last used* badge on the sign-in method chosen the last time (only when there are external providers) and one submit per form. Passkeys need a second small script, `passkey.js`; without it their button stays hidden (see [Passkeys](passkeys.md)). Animations stop when the system asks for reduced motion.

## Configuration

The design applies to the whole installation, whichever theme is the default. The configuration sets the starting point:

```json
"Coworkee": {
  "Auth": {
    "Design": {
      "Layout": "Split",
      "MediaUrl": "/media/welcome.mp4",
      "MediaSide": "Right"
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
| `Design:LoadingScreen` | background of the app's [loading screen](../modules/theming.md#loading-screen): `Animated`, `SignInMedia` (the image or video above) or `Plain` |
| `AllowDesignChanges` | `true` (default) lets administrators change the design on the Themes page; `false` keeps the configuration. Set it for the service that hosts the API, where the change is saved. |

The Aspire app host sets the same values with `options.SignIn` and serves a media path from the web app, see [Aspire app host](../hosting/aspire.md).

## On the Themes page

*Administration > Themes* starts with the card *Sign-in page*: a thumbnail per layout to pick from, the image or video address and its side, and the background of the app's [loading screen](../modules/theming.md#loading-screen). The folder button in the address field picks the image or video from the app's files; a private file is made public first (after a question), and the field gets its absolute public address, which the auth server, Keycloak and the loading screen can load without sign-in. *Save* stores the values as the app configuration section `Coworkee:Auth:Design` (table `cw.ConfigurationEntries`, see [App configuration](../modules/settings.md)); *Restore defaults* drops them, so the configured values apply again.

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

An own layout gets a generic thumbnail with its name. Themes saved with the former per theme value (`SignIn` in the theme options) are ignored; set the design once on the Themes page.

## Own designs

The layouts are Razor layouts next to the pages of the library: `Pages/_DesignCard.cshtml`, `Pages/_DesignSplit.cshtml` and `Pages/_DesignSlide.cshtml`, all inside `Pages/_Layout.cshtml`, which renders the head, the theme variables, the style sheet and the script. As with every Razor class library, a view of the auth host at the same path wins:

- **Own layout**: add `Pages/_DesignAurora.cshtml` to the auth host and set `Design:Layout` to `Aurora` (or choose it on the Themes page).
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
