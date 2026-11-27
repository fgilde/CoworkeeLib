# Replacing components

Every component of the shell can be swapped for your own: register the replacement, and wherever the original would render, yours renders instead. No fork, no copied layout.

```csharp
builder.Services.ReplaceComponent<CoworkeeUserMenu, MyUserMenu>();
```

```razor title="MyUserMenu.razor"
<MudMenu Icon="@Icons.Material.Outlined.AccountCircle" Color="Color.Inherit">
    <MudMenuItem Href="/profile">Profile</MudMenuItem>
    <MudMenuItem Href="/help">Help</MudMenuItem>
    <MudMenuItem Href="/bff/logout">Sign out</MudMenuItem>
</MudMenu>
```

The replacement receives the parameters the original would get, so it has to declare them (`[Parameter]` properties with the same names). To keep the original and only change a little, derive from it. Replacements that lead back to themselves are refused at startup.

## Layout parts

The layout is split so you can replace a part instead of the whole:

| Component | Part |
|---|---|
| `CoworkeeLayout` | the whole page frame |
| `CoworkeeAppShell` | startup and providers without the chrome, see [A layout of your own](#a-layout-of-your-own) |
| `CoworkeeAppBar` | top bar |
| `CoworkeeBrand` | logo and title in the top bar |
| `CoworkeeNavMenu` | navigation drawer content |
| `CoworkeeNavLink` | a single menu entry |
| `CoworkeeUserMenu` | user menu |
| `NotificationBell` | notifications |
| `ThemeMenu` | theme quick switch with color swatches and the color mode |
| `LanguageMenu` | language switch with flags |
| `AboutDialog` | about dialog |

Pages work the same way: replace `Profile` with your own profile page and every link to it opens yours.

## A layout of your own

For an app that must not look like the admin app at all (a customer portal next to the internal app, say), replacing parts is not enough: write your own layout and put `CoworkeeAppShell` around it. The shell does everything `CoworkeeLayout` does besides the chrome: setup check, sign-in redirect (`AllowAnonymous`), user preferences, theme, language, head script, the MudBlazor providers (theme, dialogs, popovers, snackbars), the session guard (impersonation ended, signed out elsewhere), live settings and hiding the loading screen. `CoworkeeLayout` is the admin chrome inside the same shell.

```razor title="AppLayout.razor"
@inherits LayoutComponentBase
<CoworkeeAppShell Theme="PortalTheme.Value" DarkMode="false">
    <div class="portal">
        <PortalSidebar />
        <main>@Body</main>
    </div>
</CoworkeeAppShell>
```

| Parameter | |
|---|---|
| `ChildContent` | your layout; it gets the shell as `context` (`context.Dark`, `context.ToggleDarkAsync()`) |
| `Theme` | a fixed `MudTheme` instead of the tenant's Coworkee theme (Administration, Themes); its custom CSS and logo (favicon) then do not apply |
| `DarkMode` | `true`/`false` fixes the colour mode, `null` (default) follows the user's choice or the system |

Several layouts (a main layout and an admin layout, say) share one shell when they name the same outer layout (`@layout AppLayout`): switching between them keeps theme, providers and the started state. Combine it with `Pages.Only()` (below) so that none of the library's pages is reachable in such an app.

## About dialog

The about dialog slides in from the right and renders the sections of `options.About.Sections` in order: `AboutHeader` (logo or monogram, title, version), `AboutCredits` (the libraries with logo and running version), `AboutLinks` (the app's `AboutLinks`) and `AboutFooter` (runtime and gilde.org mark). Sections are plain components, so remove, insert or swap them; credits are a list as well.

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.About.Sections.Remove(typeof(AboutCredits));
    options.About.Sections.Insert(1, typeof(LicenseSection));
    options.About.Credits.Add(new AboutCredit("MyLib", "https://example.org", Icons.Material.Outlined.Extension, AboutVersion.Of(typeof(MyLib).Assembly)));
});
```

For a completely different dialog, replace it: `builder.Services.ReplaceComponent<AboutDialog, MyAbout>();`. `MyAbout` is an ordinary `MudDialog`.

## Pages of the library

The library's admin pages live under their own prefix: `/cw/admin/users`, `/cw/admin/settings` and so on. `/admin` stays free for your own admin pages, and a library update can never take an address your app already uses. The other pages (`/files`, `/chat`, `/people/{id}`, `/profile`, `/settings`, `/notifications`, `/assistant`, `/workflows/approvals`, `/search`, `/data/export-schedules`, `/reports`) keep their short addresses.

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.PagePrefix = "/cw";                        // the default; "" keeps the old /admin/... addresses
    options.Pages.Replace<MyUsers>("users")              // your component at the library's address, with the same parameters
        .Replace("user", "/team/{Id}")                // links and old addresses go to your own page
        .Disable("backups");                            // menu entry and address are gone
});
```

An app of its own that must not reach the administration (a customer frontend) keeps only the pages it lists and turns off all others; `Only()` without keys keeps none:

```csharp
options.Pages.Only("profile");   // only the library's profile page; /cw/admin/... and the rest find nothing, their links and menu entries go
options.Pages.Only();            // no library page at all
```

Leaving the library's assembly out of the router's `AdditionalAssemblies` (and out of `AddAdditionalAssemblies` of the host) removes the catch-all route as well: then even the address of a kept page finds nothing, the router shows its `NotFound`.

Old addresses keep working: `/admin/users/…` in a bookmark, a mail or a push notification goes on to the current address. The menu, the command palette and the links between the pages ask the same registry (`CoworkeePages.Href(path)`), so a replaced or turned off page is replaced or gone everywhere. Server links (notifications, mails) use the old address; the client resolves it.

How it works: one catch-all page of the library (`CoworkeePageHost`, route `/{*Path}`) renders the library pages. Pages of your app always win over it, so an own `/admin/ai` page never clashes with the library. An address nobody knows shows the router's `NotFoundPage` if you set one, otherwise a short "Not found." in the layout.

Keys: `users`, `user`, `groups`, `roles`, `role`, `custom-permissions`, `clients`, `scopes`, `api-keys`, `sessions`, `settings`, `sign-in`, `ai-settings`, `ai-tool-calls`, `audit`, `security-log`, `request-log`, `backups`, `data-transfer`, `export-schedules`, `editions`, `languages`, `translations`, `mail-settings`, `mail-templates`, `mail-template`, `mail-campaigns`, `mail-log`, `send-notification`, `retention`, `services`, `system-status`, `tenants`, `themes`, `header`, `dialog-options`, `webhooks`, `approvals`, `assistant`, `chat`, `person`, `files`, `notifications`, `profile`, `search`, `user-settings`, and with reporting `reports`, `report`, `report-new`, `report-subscriptions`, `report-data`, with client entities `local-copies`. An unknown key stops the app at start.

### An own people profile

The person page (`/people/{id}`, key `person`) is where the *Profile* entry of every avatar menu and the `@` mentions of the chat lead. An app with its own profile of people (an HR app with employee records, say) sends them there:

```csharp
options.Pages.Replace("person", "/hr/people/{Id}");   // links go to your page; {Id} is the user id
```

Your page takes the user id and looks up its own record. If your page should live at `/people/{id}` itself, give it that route (`@page "/people/{Id:guid}"`): pages of the app win over the library's, and the links stay as they are. `options.Pages.Replace<MyPerson>("person")` renders your component at the library's address instead.

## When to replace and when to contribute

Prefer the contribution points when they fit: `INavigationContributor` for menu entries, `IAppBarContributor` for the top bar, `NavigationMenuOptions` to hide or order. Replace a component when you need different markup or behavior.
