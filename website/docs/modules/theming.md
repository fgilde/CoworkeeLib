# Theming

Themes are data: palettes for light and dark mode, typography, border radius, drawer width and app bar height, shadows, logo, custom CSS, and the app's own options. Seven built-in themes ship with the package (Coworkee, Classic, Ocean, Forest, Midnight, Sunset and High Contrast); they differ in fonts, radius, navigation width and behaviour, not only in colors.

![Theme selection in the user settings](../assets/screenshots/themes.png){ .shot }

- *Administration > Themes* (`Themes.Manage`) is a `MudExThemeEdit<CoworkeeTheme>`: create, change and delete themes with a live preview. Built-in themes stay read-only, saving one creates a copy.
- Everyone can choose the built-in themes and the *published* ones; the tenant default applies until a user picks another theme (and light, dark or system mode) in the account settings or from the palette in the app bar.
- `CoworkeeTheme` derives from `MudTheme` and adds: `ShowLogoInNav`, `ShowLogoInAppBar`, `ShowNavFilter`, `CanPinNav`, `CanChangeNavExpandMode`, `NavSingleExpand` (until the user chooses), `DenseTables`, `StripedTables`, `IsPublished`, `LogoSvg`, `CustomCss`. All of them are fields in the editor.
- Without a theme logo the app's own logo shows: `options.AppLogo = "logo.svg"` in `AddCoworkeeClient`.
- Setup offers the theme as one of its steps.
- `ThemeService` in the client applies a theme at runtime (`Apply`, `Preview`, `SetMode`).
