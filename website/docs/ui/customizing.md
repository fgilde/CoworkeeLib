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

## When to replace and when to contribute

Prefer the contribution points when they fit: `INavigationContributor` for menu entries, `IAppBarContributor` for the top bar, `NavigationMenuOptions` to hide or order. Replace a component when you need different markup or behavior.
