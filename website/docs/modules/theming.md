# Theming

Themes are data: palettes for light and dark mode, typography, border radius, drawer width and app bar height, shadows, logo, custom CSS, and the app's own options. Seven built-in themes ship with the package (Ocean, the default, Coworkee, Classic, Forest, Midnight, Sunset and High Contrast); they differ in fonts, radius, navigation width and behaviour, not only in colors.

![Theme selection in the user settings](../assets/screenshots/themes.png){ .shot }

- *Administration > Themes* (`Themes.Manage`) shows a tile per theme: light and dark colors, name, published state, *Edit*, *Make default* and *Delete* (own themes only, after a confirmation). *Edit* and *New theme* open a `MudExThemeEdit<CoworkeeTheme>` with a live preview; a new theme starts from one of the existing themes. Built-in themes stay read-only, saving one creates a copy.
- Everyone can choose the built-in themes and the *published* ones; the tenant default applies until a user picks another theme (and light, dark or system mode) in the account settings or from the palette in the app bar.
- `CoworkeeTheme` derives from `MudTheme` and adds: `ShowLogoInNav`, `ShowLogoInAppBar`, `ShowNavFilter`, `CanPinNav`, `CanChangeNavExpandMode`, `NavSingleExpand` (until the user chooses), `Dense`, `StripedTables`, `IsPublished`, `LogoSvg`, `CustomCss`. All of them are fields in the editor. `CustomCss` is a stylesheet for the whole app (rendered after the theme), edited in a growing multiline field.
- `Dense` (on by default) makes the whole UI compact: tables, lists, menus, tree views, alerts, expansion panels and checkboxes render `Dense`, inputs and selects use `Margin.Dense`, chips `Size.Small`. Forms (`MudExObjectEdit`) and the navigation menu keep their normal size. Themes saved with the former `DenseTables` option keep their value.
- The library's components inherit `CoworkeeComponentBase`, which receives the theme's `Density` as cascading value (`Density.Dense`, `Density.Margin`, `Density.Size`). Own components can do the same: `@inherits CoworkeeComponentBase` or `[CascadingParameter] Density Density { get; set; } = Density.Default;`. A model's own `IObjectMetaConfiguration<T>` replaces the dense object edit configuration for that model.
- Without a theme logo the app's own logo shows: `options.AppLogo = "logo.svg"` in `AddCoworkeeClient`.
- Setup offers the theme as one of its steps.
- The auth server's account pages follow the default theme's colors, font and logo. Their layout and media are one setting for the whole installation: the card *Sign-in page* at the top of *Administration > Themes*, see [Sign-in pages and designs](../security/sign-in-pages.md).
- The theme editor's fields come from MudEx object edit metadata. The library registers `CoworkeeThemeEditMeta` as `IObjectMetaConfiguration<CoworkeeTheme>` (the `CustomCss` field); register your own after `AddCoworkeeClient` to render fields differently (it replaces the library's):

```csharp
builder.Services.AddScoped<IObjectMetaConfiguration<CoworkeeTheme>, MyThemeEditMeta>();

public sealed class MyThemeEditMeta : IObjectMetaConfiguration<CoworkeeTheme>
{
    public Task ConfigureAsync(ObjectEditMeta<CoworkeeTheme> meta)
    {
        meta.Property(t => t.LogoSvg).WithLabel("Logo (SVG markup)");
        return Task.CompletedTask;
    }
}
```
- `ThemeService` in the client applies a theme at runtime (`Apply`, `Preview`, `SetMode`).
