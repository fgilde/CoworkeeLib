---
title: "Blazor admin UI with MudBlazor and data tables"
description: "A complete Blazor WebAssembly admin UI on MudBlazor: navigation, admin pages, data tables with facets, grouping and saved views, themes and sign-in designs."
---

# Blazor admin UI on MudBlazor

The first months of a business app often go into the frame around the actual features: a layout, a menu that respects permissions, user and role pages, settings, a table that can filter and export, a login page that does not look like a default template. None of it is the reason the app exists, and all of it has to work before the first customer sees anything.

`Coworkee.Client.Blazor` is that frame as a package. It is a Blazor WebAssembly shell built on MudBlazor and MudBlazor.Extensions, with the admin pages already in place. The app registers its own pages and menu entries and replaces what it wants to look different.

![Products table grouped by brand with the average rate per group](../assets/screenshots/products-grouped-light.webp){ .shot loading=lazy }

## A Blazor WebAssembly shell in a few lines

```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddCoworkeeClient(new Uri(builder.HostEnvironment.BaseAddress), options =>
{
    options.AppTitle = "MyApp";
    options.AppLogo = "logo.svg";
});
builder.Services.AddSingleton<INavigationContributor, MyAppNavigation>();
var host = builder.Build();
await host.Services.InitializeCoworkeeClientAsync();
await host.RunAsync();
```

The router uses `CoworkeeLayout` as default layout. With that, the app has an app bar with theme menu, dark mode, right-to-left switch, notifications and user menu, a profile page, and admin pages for users, groups, roles, settings, services, themes, mail templates, languages, translations and the audit log.

## Navigation that follows permissions

Menu entries come from `INavigationContributor` implementations. Each entry names its permission, so the menu shows only what the user may open; `NavigationMenuOptions` orders groups, hides entries or moves a built-in admin page into another group.

```csharp
public IEnumerable<CoworkeeNavItem> Items =>
[
    new("Products", "/catalog/products", Icons.Material.Outlined.ViewCarousel, CatalogPermissions.Products.View, Group: "Catalog"),
    new("Brands", "/catalog/brands", Icons.Material.Outlined.Sell, CatalogPermissions.Brands.View, Group: "Catalog", Order: 1),
];
```

The menu is a `MudExTreeView` with a filter box that highlights matches. Users switch between tree, list and flat list, and pin the drawer as an icon strip or let it open as an overlay. When roles or grants change, the client reloads its permissions and the menu follows without a page reload.

## A data table for OData sets

`CoworkeeDataTable<T>` is a MudBlazor data grid on top of an [OData set](data.md). The server pages, sorts, filters and counts. The table adds what users of list screens ask for:

- **Facets** from `[ProvideFacet]` on the entity, as pills above the rows or in a side panel, with views that fit the values: check lists, chips, range sliders over a histogram, date presets.
- **Column filters** in every header, with editors for text, numbers, dates and enums. A column with a facet shows the facet's options and counts.
- **Advanced filter** with the query builder: nested *all / any* groups.
- **Grouping** by dragging a header onto the group zone, nested over several columns, with aggregates per group.
- **Saved views**: search, facets, filters, column order and widths, grouping, sorting and density under a name, stored in the user's settings so they follow the user to every device. A link button copies an address that opens the table exactly as it is.
- **Pager or endless scrolling**, chosen per table or by a setting that administrators set for everyone and users can override.
- **Export** as CSV, JSON and Excel of all rows of the current filter, and Excel import with `Importable="true"`.

```razor
<CoworkeeDataTable T="ProductDto" EntitySet="Products" Expand="Brand" SearchFields="SearchFields"
                   CreatePermission="@CatalogPermissions.Products.Create" OnCreate="CreateAsync" OnEdit="EditAsync">
    <Columns>
        <PropertyColumn Property="p => p.Name" />
        <PropertyColumn Property="p => p.Brand!.Name" Title="Brand" />
    </Columns>
</CoworkeeDataTable>
```

Tables over `[Realtime]` entities reload when someone else changes a row.

## Edit forms with MudEx object edit and slide-in dialogs

For plain models, `Dialogs.ShowEditAsync(title, model, save)` generates the form with `MudExObjectEditForm`. It opens as a full-height side sheet that slides in from the side the user clicked on, with two columns on wide screens. With a save function the dialog stays open and shows the API's validation messages when saving fails. Editors such as `ODataPicker` for a foreign key are set per property in the meta configuration.

## Themes: Ocean by default, per user, with your logo

Themes are data: palettes for light and dark mode, typography, radius, navigation width, density, logo and custom CSS. Seven themes ship with the package, Ocean is the default. Administrators edit them with a live preview, publish them and choose the organisation's default; each user picks a published theme and light, dark or system mode in the account settings. An app adds its own colors with `ThemingOptions`:

```csharp
context.Services.Configure<ThemingOptions>(themes =>
{
    themes.DefaultTheme = "Shop";
    themes.Themes.Add(new AppTheme("Shop", "Ocean", new Dictionary<string, string> { ["Primary"] = "#0a7d5a" }));
});
```

![Themes page with the sign-in page layouts Card, Split and Slide and a tile per theme](../assets/screenshots/themes-light.webp){ .shot loading=lazy }

While the WebAssembly app downloads, `CoworkeeLoadingScreen` shows a server-rendered screen in the colors and with the logo of the default theme. The ring around the logo shows the real download progress; if the start fails, the screen turns into an error message with *Reload*.

## Sign-in designs: Card, Split and Slide

The account pages of the auth server follow the default theme's colors, font and logo. Three layouts ship: `Card`, `Split` with an image or video beside the form, and `Slide`, the default, where a colored panel moves to the other side between sign-in and registration. Administrators of the system organisation pick the layout on the Themes page; an own layout is a Razor file in the auth host.

![Slide layout halfway through the move from sign-in to registration](../assets/screenshots/signin-sliding-light.webp){ .shot loading=lazy }

## Replace any component

Every component of the shell can be swapped without forking the layout:

```csharp
builder.Services.ReplaceComponent<CoworkeeUserMenu, MyUserMenu>();
```

The layout is split into parts (`CoworkeeAppBar`, `CoworkeeBrand`, `CoworkeeNavMenu`, `CoworkeeUserMenu`, `NotificationBell`, ...), so a change replaces one part instead of the whole frame. For menu entries and app bar items the contribution points (`INavigationContributor`, `IAppBarContributor`) are usually enough.

## AirWorks: a web desktop as second client

Next to the Blazor app, `coworkee new Shop --airworks` adds a web desktop client built on AirWorks, with windows, a taskbar and several desktops. It signs in through the same auth server, talks to the same API and brings most of the Blazor app's features: settings, files, data tables, chat, reports, the AI assistant and the administration.

## Where to go next

- [Client and layout](../ui/client.md): setup, navigation, app bar, building blocks
- [Data tables](../ui/data-table.md): parameters, facets, column filters, views and edit dialogs
- [Replacing components](../ui/customizing.md): layout parts and the about dialog
- [Theming](../modules/theming.md): built-in themes, app themes, logo and loading screen
- [Sign-in pages and designs](../security/sign-in-pages.md): layouts, configuration and own designs
- [AirWorks client](../web/airworks.md): the web desktop and writing your own AirWorks apps
