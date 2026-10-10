# Sign-in pages and designs

The account pages of the auth server (sign-in, registration, two-step verification, forgotten and new password, consent, sign-out, the app launcher and the account security pages) follow the app's theme: the default theme of the system organisation gives them colors for light and dark mode, the font, the border radius and the logo, the configuration the app name. The color mode setting (`Theme.Mode`: system, light or dark, the user's or the installation's) applies as well.

## Layouts

Three layouts ship with the library:

| Layout | Look |
|---|---|
| `Card` (default) | a centered card |
| `Split` | full page: image or video on one side, the form on the other |
| `Slide` | a card with a colored panel next to the form. On the sign-in page the panel invites to create an account (when registration is on); on the registration page it moves to the other side and links back to sign-in. Browsers with cross-document view transitions animate the move. |

The pages work without script. A small script adds what a browser alone cannot: a button that shows the password, a hint while Caps Lock is on, a *Last used* badge on the sign-in method chosen the last time (only when there are external providers) and one submit per form. Animations stop when the system asks for reduced motion.

## Configuration

```json
"Coworkee": {
  "Auth": {
    "Design": {
      "Layout": "Split",
      "MediaUrl": "/media/welcome.mp4",
      "MediaSide": "Right"
    },
    "AllowThemeDesign": true
  }
}
```

| Option | Meaning |
|---|---|
| `Design:Layout` | `Card`, `Split`, `Slide` or the name of an own layout; unknown names fall back to `Card` |
| `Design:MediaUrl` | an image, or a video for `.mp4`, `.webm`, `.ogg` and `.mov`; a path on the auth server or an `http(s)` address, whose origin enters `img-src` and `media-src` of the content security policy |
| `Design:MediaSide` | `Left` (default) or `Right`; with `Slide` the panel of the registration page sits on the other side |
| `AllowThemeDesign` | `true` (default) lets administrators choose layout and media per theme; `false` keeps the configuration |

## In the theme editor

*Administration > Themes > Edit* shows a section *Sign-in page* above the theme editor: layout, image or video address and side. The values are stored in the theme's options under `SignIn`; empty fields keep the configuration. The default theme of the system organisation decides. The client options control the section:

```csharp
builder.Services.AddCoworkeeClient(options =>
{
    options.AllowThemeSignInDesign = false;   // hides the section
    options.SignInLayouts.Add("Aurora");      // offers an own layout
});
```

Switch `AllowThemeSignInDesign` together with `Coworkee:Auth:AllowThemeDesign` on the auth server: the client shows the section, the auth server applies it.

## Own designs

The layouts are Razor layouts next to the pages of the library: `Pages/_DesignCard.cshtml`, `Pages/_DesignSplit.cshtml` and `Pages/_DesignSlide.cshtml`, all inside `Pages/_Layout.cshtml`, which renders the head, the theme variables, the style sheet and the script. As with every Razor class library, a view of the auth host at the same path wins:

- **Own layout**: add `Pages/_DesignAurora.cshtml` to the auth host and set `Design:Layout` to `Aurora` (or choose it in the theme editor).
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
