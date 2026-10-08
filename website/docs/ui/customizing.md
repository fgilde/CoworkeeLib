# Replacing components

Every component of the shell can be swapped for your own, the way ABP replaces components: register the replacement, and wherever the original would render, yours renders instead. No fork, no copied layout.

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

## When to replace and when to contribute

Prefer the contribution points when they fit: `INavigationContributor` for menu entries, `IAppBarContributor` for the top bar, `NavigationMenuOptions` to hide or order. Replace a component when you need different markup or behavior.
