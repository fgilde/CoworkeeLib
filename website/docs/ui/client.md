# Client and layout

`Coworkee.Client.Blazor` is a complete WebAssembly shell: layout, navigation, user menu, notifications, admin pages for users, groups, roles, settings, services, themes, mail templates and audit log. The app adds its pages and menu entries.

```csharp title="MyApp.Web.Client/Program.cs"
var builder = WebAssemblyHostBuilder.CreateDefault(args);
var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.AppTitle = "MyApp";
    options.AppDescription = "Modular business app for Blazor, built on Coworkee.";
    options.AboutLinks.Add(new AboutLink("Documentation", "https://framework.coworkee.de/"));
});
builder.Services.AddHttpClient<ICatalogApi, CatalogApi>(client => client.BaseAddress = baseAddress);
builder.Services.AddSingleton<INavigationContributor, MyAppNavigation>();
builder.Services.Configure<NavigationMenuOptions>(MyAppNavigation.Order);
var host = builder.Build();
await host.Services.InitializeCoworkeeClientAsync();
await host.RunAsync();
```

```razor title="Routes.razor"
@inject CoworkeeLocalizer L
<Router AppAssembly="typeof(Program).Assembly" AdditionalAssemblies="new[] { typeof(CoworkeeClientOptions).Assembly }">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(CoworkeeLayout)">
            <NotAuthorized><NotAllowed PageType="routeData.PageType" /></NotAuthorized>
        </AuthorizeRouteView>
    </Found>
    <NotFound>
        <LayoutView Layout="typeof(CoworkeeLayout)"><MudText>@L["Not found."]</MudText></LayoutView>
    </NotFound>
</Router>
```

![Dashboard of the template](../assets/screenshots/dashboard.png){ .shot }

## Navigation

```csharp
internal sealed class MyAppNavigation : INavigationContributor
{
    private const string CatalogManagement = "Catalog Management";

    public static void Order(NavigationMenuOptions menu) => menu
        .OrderGroup("Personal", 0)
        .OrderGroup(NavigationGroups.Administration, 2)
        .OrderGroup(CatalogManagement, 3);

    public IEnumerable<CoworkeeNavItem> Items =>
    [
        new("Dashboard", "/dashboard", Icons.Material.Outlined.Dashboard, CatalogPermissions.Dashboards.View, Group: "Personal"),
        new("Products", "/catalog/products", Icons.Material.Outlined.ViewCarousel, CatalogPermissions.Products.View, Group: CatalogManagement),
        new("Brands", "/catalog/brands", Icons.Material.Outlined.Sell, CatalogPermissions.Brands.View, Group: CatalogManagement, Order: 1),
    ];
}
```

| `NavigationMenuOptions` | |
|---|---|
| `OrderGroup(name, order)` | position of a group; unknown groups come last, by name |
| `Hide(hrefs)` | removes entries, also the built-in admin pages |
| `ShowHome`, `HomeTitle` | the home link at the top |
| `IconForGroup(name, icon)` | the icon of a group node |
| `Place(href, group, title?, order?)` | moves a link of the library into another group; a group path like `Administration/Localization` nests |

The admin pages of the library sit in groups below **Administration**: `NavigationGroups.Identity` (users, groups, roles, applications, scopes), `NavigationGroups.System` (settings, mail settings, services, tenants, editions, backups), `NavigationGroups.Communication` (mail templates, mail log), `NavigationGroups.Localization` (languages, translations) `NavigationGroups.Monitoring` (audit log, AI tool calls) and `NavigationGroups.UserInterface` (themes, header). Nested groups sort within their top level group, which keeps the place `OrderGroup` gives it.

The menu is a `MudExTreeView`: groups are nodes, the filter box highlights matches and opens their groups, and the view can switch between tree, list and flat list. Pinned, the drawer stays as an icon strip under the app bar and the menu button widens it; unpinned, it opens as an overlay and hides again. Whether one group or several stay open is a second toggle. Both choices are stored in the browser.

Entries with `HostOnly: true` serve the whole installation and show only to users of the system organisation; the library marks Tenants, Editions, Applications, Scopes, Services and Backups that way, and the settings page shows its typed tabs and the System tab only there. The client knows the organisation from `/bff/user` (`SystemTenant`, from the `system_tenant` claim of the auth server); sign-ins from before that claim show everything as before.

For pure UI cases an entry can decide itself: `Visible: sp => sp.GetRequiredService<MyTips>().HasAnyAsync()` hides it when the callback answers false. The menu and the command palette ask it whenever they build (after sign-in, permission and feature changes), so keep it cheap and cache what it loads. It only hides; when the server must agree, use a [computed permission](../security/permissions.md#computed-permissions) instead.

![Collapsed navigation](../assets/screenshots/mini-drawer.png){ .shot }

## App bar

The app bar shows the search button of the [command palette](command-palette.md) (Ctrl+K), the theme menu, right to left switch, dark mode, notifications, user menu and the about dialog; on phones the secondary entries share one menu, see [Phones, installable app and push](phones-and-pwa.md). Add your own components:

```csharp
internal sealed class LanguageBar : IAppBarContributor
{
    public IEnumerable<AppBarItem> Items => [new AppBarItem(typeof(LanguageSwitch), Order: 10, Id: "language-switch", Title: "Language switch")];
}
```

App items sit between the search and the language menu, ordered by `Order`. `Id` is the item's key in the header configuration (the component's type name when left out; keep it stable), `Title` its name on the admin page.

### Managed header

Under *Administration → User interface → Header* (`/admin/header`, `Settings.Manage`, system organisation only) an administrator reorders every item of the bar, the library's and the apps', and puts each one in the bar, in the overflow menu (⋮) or nowhere: by dragging it within or between the three areas *In the bar*, *In the overflow menu* and *Hidden*, or with the arrow and placement buttons of each row (keyboard). The lock of a row (*Visible for*) opens a sheet from the right that limits the item to users with permissions: with several, either any or all of them are needed. Without permissions everyone sees it; the restriction comes on top of an item's own rules (notifications and assistant still need a signed-in user), and it follows changes of the user's permissions at once. The account menu always stays in the bar and visible to everyone. *Reset to defaults* goes back to the built-in order. The choice is the global setting `Header.Items`, a JSON list in bar order:

```json
[{"id":"search","placement":"Bar"},{"id":"about","placement":"Overflow","permissions":["Settings.Manage","Audit.View"],"match":"All"},{"id":"rtl","placement":"Hidden"}]
```

`permissions` and `match` (`Any`, the default, or `All`) are optional; values saved without them keep working.

The library's ids are `search`, `language`, `theme`, `rtl`, `dark-mode`, `notifications`, `role`, `user`, `assistant` and `about`; `role` is the switch of the [active role](../modules/realtime.md#active-role), shown to users with several roles. Items the list does not name (an app added later) appear after the item before them in the default order; names of items that are gone are ignored; an invalid value falls back to the default header. Saved values reach open pages at once. Hidden or in the overflow menu, the search keeps Ctrl+K. On phones language, theme, direction, colour mode and about still fold into the overflow menu.

The same page sets where the [assistant](../modules/ai.md#where-the-assistant-shows) shows.

The user menu holds *My profile*, *My settings*, *Password and security* (the account pages of the auth server), *Install app* while the browser offers it, and *Sign out*. While someone is [signed in as another user](../features/identity.md#log-in-as-another-user), the app bar shows *Impersonated by …* and the menu *Back to my account*.

![About dialog](../assets/screenshots/about.png){ .shot }

## Dialogs

Every dialog of the library takes its options (`DialogOptionsEx` of MudBlazor.Extensions) from one place, `DialogOptionsContributor`, by kind (`DialogPart`):

| Part | Used for | Library default |
|---|---|---|
| `SideSheet` | edit forms (`ShowEditAsync`), `ShowSideSheetAsync`, file preview, history | full height, slides in from the side the user clicked on |
| `RightSheet` | about dialog, version history, mail preview | narrow, full height, slides in from the right |
| `Small` | confirmations (`ConfirmAsync`) and other small centered dialogs | flips in |
| `Fullscreen` | code editor | whole screen |
| `Progress` | export progress | cannot be closed |
| `ListItem` | an item of a list in a settings form | resizable, draggable |

An app changes parts by deriving from the contributor and registering it (the template has an empty `MyAppDialogOptions`):

```csharp
internal sealed class MyAppDialogOptions : DialogOptionsContributor
{
    public override DialogOptionsEx Small() => Perspective(base.Small());
    public override DialogOptionsEx Fullscreen() => Perspective(base.Fullscreen());
    public override DialogOptionsEx Progress() => Perspective(base.Progress());
    public override DialogOptionsEx ListItem() => Perspective(base.ListItem());

    private static DialogOptionsEx Perspective(DialogOptionsEx options)
    {
        options.Animations = [AnimationType.Perspective3d];
        return options;
    }
}

builder.Services.AddSingleton<DialogOptionsContributor, MyAppDialogOptions>();
```

Each method returns a new instance. Own dialogs get the same options with `EditDialogExtensions.Small()`, `RightSheet()`, `await SideSheetAsync()` or `CoworkeeDialogOptions.For(DialogPart.X)`.

### Dialog options for administrators

With `public override bool AdminEditable => true;` the navigation gets *Administration → User interface → Dialog options* (`/admin/dialog-options`, `Settings.Manage`, system organisation only): one tab per part with all options in an object editor, *Preview* to open a dialog with them and *Reset to the app's options*. Only parts that differ from the app are saved, in the global setting `Dialogs.Options` (JSON per part, e.g. `{"Small":{"maxWidth":"Large"}}`); a saved part replaces the app's part as a whole, and a side sheet still opens on the clicked side. Saved values reach open pages at once. Without `AdminEditable` (the default) the page is not listed and the setting is ignored.

## Head script

With `AddCoworkeeHeadScript()` on the server, administrators keep a script such as a tag manager in a setting ([Settings](../modules/settings.md#head-script)), and `CoworkeeLayout` puts it into the page; the client needs nothing. Once the app has started (theme and client settings loaded) and when `HeadScript.Enabled` is on and `HeadScript.Value` is not empty, the layout hands the HTML to `coworkee.js` once per page load:

- inline JavaScript (`<script>` without `src`, without a type or of a JavaScript type) runs in the global scope through `eval`; an error is written to the browser console as a warning and does not stop the app,
- `<script src>` and scripts of other types become new tags in `<head>` with their attributes (`async`, `id`, ...),
- `<noscript>` and `<iframe>` go to the end of `<body>`.

A marker (`<meta name="cw-head-script">`) keeps it to one injection per page load, so a changed script applies after a reload. Pages opened with `?embed=1` get it as well. The script runs after the app has rendered: snippets that rely on `document.write` or `document.currentScript` do not work.

**Content-Security-Policy**: the policy of `UseCoworkeeSecurityHeaders` allows no inline script, but `'unsafe-eval'` (Blazor's libraries need it), which is why inline code runs through `eval`. A script loaded from another host (`https://www.googletagmanager.com`) needs that host in `script-src`, and whatever it sends or shows needs `connect-src`, `img-src` or `frame-src`: `Coworkee:Bff:SecurityHeaders:Sources`, see [security headers of the web app](../security/authentication.md#security-headers-of-the-web-app). An own policy without `'unsafe-eval'` runs no inline head script.

## Embedded pages

`?embed=1` on the first address shows only the page: no app bar, menu or assistant, less padding. Windows of another client use it, for example [pages of the web app in AirWorks](../web/airworks.md#pages-of-the-web-app-embedapp). The layout keeps the mode while the user navigates inside the frame.

## Calling your API

Typed clients derive from `ApiClientBase`. It sends the CSRF header the BFF requires and turns problem details into `ApiException`:

```csharp
internal sealed class CatalogApi(HttpClient http) : ApiClientBase(http), ICatalogApi
{
    public Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default) => GetAsync<DashboardDto>("api/v1/dashboard", cancellationToken);

    public Task DeleteBrandsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/v1/brands/delete", new IdsRequest(ids), cancellationToken);
}
```

`Snackbar.RunAsync(() => api.SaveAsync(...), "Saved")` runs a call and shows success or the validation messages of a failure.

## Current user

`ClientUser.From(user)` reads the signed-in user from a `ClaimsPrincipal` (`IsAuthenticated`, `UserId`, `TenantId`, `Email`, `DisplayName`); with the cascading authentication state that is all a page needs. Services and pages without it inject the scoped `CurrentUserInfo` (registered by `AddCoworkeeClient`):

```csharp
var me = await currentUser.GetAsync();
if (me.UserId is { } userId) { ... }
currentUser.Changed += user => ...; // sign-in, sign-out, permission reissue
```

Do not parse the `sub` claim yourself.

## People

`UserAvatar` shows a user's picture or initials with the presence dot (`ShowPresence`), `UserCard` picture and name with an optional second line. For other people the avatar opens a menu with *Chat* and *Profile* when the user may chat; set `OfferChat="false"` where it sits in something that reacts to clicks. Both load in batches and update for everyone the moment someone changes name or picture on the profile page (`tenant:users` realtime topic).

![Profile page](../assets/screenshots/profile.png){ .shot }

```razor
<UserCard UserId="entry.ActorId">@entry.CreatedAt.ToString("g")</UserCard>
<UserAvatar UserId="message.FromUserId" Size="Size.Small" ShowTooltip="true" />
```

## Account page

`/profile` holds the tabs Profile (picture, name, phone, address), Security, Notifications, Settings and Privacy (keys `security`, `notifications`, `settings`, `privacy`); `/profile/{key}` opens one directly. Register a `ProfileTab` to add your own:

```csharp
builder.Services.AddSingleton(new ProfileTab("Documents", "documents", typeof(MyDocuments), DocumentPermissions.Documents.View));
```

## Building blocks

| Component | Use |
|---|---|
| `PageHeader` | title, description, actions on the right; sets the browser title. Pass text keys (`Title="Brands"`), it translates them itself; so does `CreateText` of `CoworkeeDataTable` |
| `StickyMergeTabs` | wraps a `<MudTabs>`: its tab bar sticks below the app bar and shrinks to its tabs, so a sticky `MudExObjectEditForm` toolbar in the panel shows next to it; not on narrow screens |
| `PermissionGate`, `PermissionView` | render content only with a permission |
| `PermissionPicker` | picks permissions, see below |
| `FeatureGate` | renders content only while a bool feature is on (`Disabled` for the other case) |
| `GroupScopePicker` | lets users narrow lists to some of their groups, see [group tree and scope](../features/identity.md#group-tree-and-scope) |
| `RealtimeSubscription` | calls back when the server publishes on a topic, e.g. `type:Brand` |
| `EntityWatch` | keeps a detail page in step with its entry, goes back when it is deleted |
| `NotAllowed` | what the router shows instead of a forbidden page, with a switch to a role that grants it |
| `ActiveRoleMenu` | the header item `role`: the [active role](../modules/realtime.md#active-role) (`Nested` in the overflow menu) |
| `CoworkeeDataTable<T>` | [data tables](data-table.md) |
| `ShowEditAsync` | edit dialog generated from a model |
| `AuditTimeline`, `VersionHistory` | change history of an entity |
| `ResourcePermissionsPanel` | grants on a single resource |

### Permission picker

`PermissionPicker` lists the permission definitions of the server (`GetPermissionDefinitionsAsync`, the same the role page shows, custom ones included) by group with their display names and a search; the chosen ones show as removable chips, permissions that no longer exist by their name. With `ShowMatch` and two or more chosen it also asks whether any or all of them are needed. `PermissionStore.HasAsync(permissions, match)` checks such a choice for the signed-in user.

```razor
<PermissionPicker @bind-Value="_permissions" ShowMatch="true" @bind-Match="_match" />

@code {
    private IReadOnlyList<string> _permissions = [];
    private PermissionMatch _match;
}
```