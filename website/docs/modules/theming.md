# Theming

Themes are data: palette for light and dark mode, typography, border radius, logo, custom CSS. Seven built-in themes ship with the package (Coworkee, Classic, Ocean, Forest, Midnight, Sunset and High Contrast); administrators create their own per tenant in the theme editor with a live preview.

![Theme selection in the user settings](../assets/screenshots/themes.png){ .shot }

- The tenant default applies to everyone; users pick another theme and light, dark or system mode on *My settings* or from the palette in the app bar.
- Setup offers the theme as one of its steps.
- `ThemeService` in the client applies a theme at runtime (`Apply`, `Preview`, `SetMode`).
