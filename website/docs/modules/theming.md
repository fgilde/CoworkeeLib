# Theming

Themes are data: palettes for light and dark mode, typography, border radius, drawer width and app bar height, shadows, logo, custom CSS, and the app's own options. Seven built-in themes ship with the package (Ocean, the default, Coworkee, Classic, Forest, Midnight, Sunset and High Contrast); they differ in fonts, radius, navigation width and behaviour, not only in colors.

![Theme selection in the user settings](../assets/screenshots/themes.png){ .shot }

- *Administration > Themes* (`Themes.Manage`) shows a tile per theme: light and dark colors, name, published state, *Edit*, *Make default* and *Delete* (own themes only, after a confirmation). *Edit* and *New theme* open a `MudExThemeEdit<CoworkeeTheme>` with a live preview; a new theme starts from one of the existing themes. Built-in themes stay read-only, saving one creates a copy.
- Everyone can choose the built-in themes and the *published* ones; the tenant default applies until a user picks another theme (and light, dark or system mode) in the account settings or from the palette in the app bar.
- `CoworkeeTheme` derives from `MudTheme` and adds: `ShowLogoInNav`, `ShowLogoInAppBar`, `ShowNavFilter`, `CanPinNav`, `CanChangeNavExpandMode`, `NavSingleExpand` (until the user chooses), `Dense`, `StripedTables`, `IsPublished`, `Logo`, `CustomCss`. All of them are fields in the editor. `CustomCss` is a stylesheet for the whole app (rendered after the theme), edited in a growing multiline field.
- `Dense` (on by default) makes the whole UI compact: tables, lists, menus, tree views, alerts, expansion panels and checkboxes render `Dense`, inputs and selects use `Margin.Dense`, chips `Size.Small`. Forms (`MudExObjectEdit`) and the navigation menu keep their normal size. Themes saved with the former `DenseTables` option keep their value.
- The library's components inherit `CoworkeeComponentBase`, which receives the theme's `Density` as cascading value (`Density.Dense`, `Density.Margin`, `Density.Size`). Own components can do the same: `@inherits CoworkeeComponentBase` or `[CascadingParameter] Density Density { get; set; } = Density.Default;`. A model's own `IObjectMetaConfiguration<T>` replaces the dense object edit configuration for that model.
- `Logo` is SVG markup or an image data URL (PNG, JPEG, WebP or SVG, at most 512 KB; SVG is rejected when it contains scripts, event handlers or embedded documents). The editor shows it with `ThemeLogoEdit`: a preview, *Choose logo* (upload, or the configured cloud drives) and *Use app logo*. Raster images larger than 512 px are shrunk in the browser first. The logo appears in the app bar, the navigation, setup, the about dialog and the account pages, and is the favicon while it is set.
- Without a theme logo the app's own logo shows: `options.AppLogo = "logo.svg"` in `AddCoworkeeClient` (`Coworkee:Auth:LogoUrl` for the account pages).
- On the server `ThemeLogo.TryRead(theme.Logo, out var bytes, out var contentType)` (`Coworkee.Contracts.Theming`) turns either form into bytes and content type, for example to write the logo to a file.
- The column `LogoSvg` of the theme table is now `Logo`: add a migration to an existing app (EF Core renames the column when you edit the generated migration to use `RenameColumn`).
- Setup offers the theme as one of its steps.
- The auth server's account pages follow the default theme's colors, font and logo. Their layout and media are one setting for the whole installation: the card *Sign-in page* at the top of *Administration > Themes*, see [Sign-in pages and designs](../security/sign-in-pages.md).
- The theme editor's fields come from MudEx object edit metadata. The library registers `CoworkeeThemeEditMeta` as `IObjectMetaConfiguration<CoworkeeTheme>` (the `Logo` and `CustomCss` fields); register your own after `AddCoworkeeClient` to render fields differently (it replaces the library's):

```csharp
builder.Services.AddScoped<IObjectMetaConfiguration<CoworkeeTheme>, MyThemeEditMeta>();

public sealed class MyThemeEditMeta : IObjectMetaConfiguration<CoworkeeTheme>
{
    public Task ConfigureAsync(ObjectEditMeta<CoworkeeTheme> meta)
    {
        meta.Property(t => t.Logo).WithLabel("Company logo");
        return Task.CompletedTask;
    }
}
```
- `ThemeService` in the client applies a theme at runtime (`Apply`, `Preview`, `SetMode`).

## Default theme and app themes

`ThemingOptions` picks the default theme of the installation and adds themes of the app. An app theme is a built-in theme with other colors; it is seeded and kept like the built-in ones (read-only in the app, offered in setup). A changed `Primary` or `Secondary` gets a readable contrast text unless the palette names one.

```csharp title="MyApp.Infrastructure/MyAppInfrastructureModule.cs"
context.Services.Configure<ThemingOptions>(themes =>
{
    themes.DefaultTheme = "Shop";
    themes.Themes.Add(new AppTheme("Shop", "Ocean", new Dictionary<string, string> { ["Primary"] = "#0a7d5a", ["Secondary"] = "#ff8f00" }));
});
```

An unknown `DefaultTheme` keeps Ocean. An app theme with the name of a built-in or another app theme, or based on an unknown theme, stops the host at start with a message. The default applies until an organisation chooses its own theme (setup or *Make default*). `coworkee new --theme Forest --primary-color "#0a7d5a"` writes this for you.
