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
| `CoworkeeAppBar` | top bar |
| `CoworkeeBrand` | logo and title in the top bar |
| `CoworkeeNavMenu` | navigation drawer content |
| `CoworkeeNavLink` | a single menu entry |
| `CoworkeeUserMenu` | user menu |
| `NotificationBell` | notifications |
| `ThemeMenu` | theme quick switch |
| `AboutDialog` | about dialog |

Pages work the same way: replace `Profile` with your own profile page and every link to it opens yours.

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

The library's admin pages live under their own prefix: `/cw/admin/users`, `/cw/admin/settings` and so on. `/admin` stays free for your own admin pages, and a library update can never take an address your app already uses. The other pages (`/files`, `/chat`, `/profile`, `/settings`, `/notifications`, `/assistant`, `/workflows/approvals`, `/reports`) keep their short addresses.

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.PagePrefix = "/cw";                        // the default; "" keeps the old /admin/... addresses
    options.Pages.Replace<MyUsers>("users")              // your component at the library's address, with the same parameters
        .Replace("user", "/people/{Id}")                // links and old addresses go to your own page
        .Disable("backups");                            // menu entry and address are gone
});
```

Old addresses keep working: `/admin/users/…` in a bookmark, a mail or a push notification goes on to the current address. The menu, the command palette and the links between the pages ask the same registry (`CoworkeePages.Href(path)`), so a replaced or turned off page is replaced or gone everywhere. Server links (notifications, mails) use the old address; the client resolves it.

How it works: one catch-all page of the library (`CoworkeePageHost`, route `/{*Path}`) renders the library pages. Pages of your app always win over it, so an own `/admin/ai` page never clashes with the library. An address nobody knows shows the router's `NotFoundPage` if you set one, otherwise a short "Not found." in the layout.

Keys: `users`, `user`, `groups`, `roles`, `role`, `clients`, `scopes`, `api-keys`, `settings`, `ai-settings`, `ai-tool-calls`, `audit`, `backups`, `data-transfer`, `editions`, `languages`, `translations`, `mail-settings`, `mail-templates`, `mail-template`, `mail-campaigns`, `mail-log`, `services`, `system-status`, `tenants`, `themes`, `webhooks`, `approvals`, `assistant`, `chat`, `files`, `notifications`, `profile`, `search`, `user-settings`, and with reporting `reports`, `report`, `report-data`. An unknown key stops the app at start.

## When to replace and when to contribute

Prefer the contribution points when they fit: `INavigationContributor` for menu entries, `IAppBarContributor` for the top bar, `NavigationMenuOptions` to hide or order. Replace a component when you need different markup or behavior.
