# Themes

Themes sind Daten: Paletten für hellen und dunklen Modus, Typografie, Eckenradius, Breite der Navigation und Höhe der App-Leiste, Schatten, Logo, eigenes CSS und die Optionen der App. Sieben Themes sind eingebaut (Coworkee, Classic, Ocean, Forest, Midnight, Sunset und High Contrast); sie unterscheiden sich in Schrift, Radius, Breite und Verhalten der Navigation, nicht nur in den Farben.

![Theme-Auswahl in den Benutzereinstellungen](../assets/screenshots/themes.png){ .shot }

- *Administration > Themes* (`Themes.Manage`) ist ein `MudExThemeEdit<CoworkeeTheme>`: Themes anlegen, ändern und löschen mit Live-Vorschau. Eingebaute Themes bleiben unverändert, Speichern legt eine Kopie an.
- Alle Benutzer können die eingebauten und die *veröffentlichten* Themes wählen; der Standard des Mandanten gilt, bis ein Benutzer in den Kontoeinstellungen oder über die Palette in der App-Leiste ein anderes Theme (und hellen, dunklen oder System-Modus) wählt.
- `CoworkeeTheme` erbt von `MudTheme` und ergänzt: `ShowLogoInNav`, `ShowLogoInAppBar`, `ShowNavFilter`, `CanPinNav`, `CanChangeNavExpandMode`, `NavSingleExpand` (bis der Benutzer wählt), `DenseTables`, `StripedTables`, `IsPublished`, `LogoSvg`, `CustomCss`. Alle sind Felder im Editor.
- Ohne Logo im Theme erscheint das Logo der App: `options.AppLogo = "logo.svg"` in `AddCoworkeeClient`.
- Das Setup bietet das Theme als eigenen Schritt an.
- `ThemeService` im Client wendet ein Theme zur Laufzeit an (`Apply`, `Preview`, `SetMode`).
