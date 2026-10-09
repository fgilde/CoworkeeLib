# Themes

Themes sind Daten: Paletten für hellen und dunklen Modus, Typografie, Eckenradius, Breite der Navigation und Höhe der App-Leiste, Schatten, Logo, eigenes CSS und die Optionen der App. Sieben Themes sind eingebaut (Coworkee, Classic, Ocean, Forest, Midnight, Sunset und High Contrast); sie unterscheiden sich in Schrift, Radius, Breite und Verhalten der Navigation, nicht nur in den Farben.

![Theme-Auswahl in den Benutzereinstellungen](../assets/screenshots/themes.png){ .shot }

- *Administration > Themes* (`Themes.Manage`) zeigt je Theme eine Kachel: helle und dunkle Farben, Name, Veröffentlichungsstatus, *Bearbeiten*, *Als Standard festlegen* und *Löschen* (nur eigene Themes, nach Rückfrage). *Bearbeiten* und *Neues Theme* öffnen einen `MudExThemeEdit<CoworkeeTheme>` mit Live-Vorschau; ein neues Theme startet von einem der vorhandenen. Eingebaute Themes bleiben unverändert, Speichern legt eine Kopie an.
- Alle Benutzer können die eingebauten und die *veröffentlichten* Themes wählen; der Standard des Mandanten gilt, bis ein Benutzer in den Kontoeinstellungen oder über die Palette in der App-Leiste ein anderes Theme (und hellen, dunklen oder System-Modus) wählt.
- `CoworkeeTheme` erbt von `MudTheme` und ergänzt: `ShowLogoInNav`, `ShowLogoInAppBar`, `ShowNavFilter`, `CanPinNav`, `CanChangeNavExpandMode`, `NavSingleExpand` (bis der Benutzer wählt), `Dense`, `StripedTables`, `IsPublished`, `LogoSvg`, `CustomCss`. Alle sind Felder im Editor.
- `Dense` (standardmäßig an) macht die ganze Oberfläche kompakt: Tabellen, Listen, Menüs, Baumansichten, Hinweise, Ausklappbereiche und Checkboxen werden `Dense` dargestellt, Eingabefelder und Auswahllisten mit `Margin.Dense`, Chips mit `Size.Small`. Formulare (`MudExObjectEdit`) und das Navigationsmenü behalten ihre normale Größe. Themes, die mit der früheren Option `DenseTables` gespeichert wurden, behalten ihren Wert.
- Die Komponenten der Bibliothek erben von `CoworkeeComponentBase`, das die `Density` des Themes als Cascading Value erhält (`Density.Dense`, `Density.Margin`, `Density.Size`). Eigene Komponenten können das genauso nutzen: `@inherits CoworkeeComponentBase` oder `[CascadingParameter] Density Density { get; set; } = Density.Default;`. Eine eigene `IObjectMetaConfiguration<T>` eines Modells ersetzt die kompakte Formular-Konfiguration für dieses Modell.
- Ohne Logo im Theme erscheint das Logo der App: `options.AppLogo = "logo.svg"` in `AddCoworkeeClient`.
- Das Setup bietet das Theme als eigenen Schritt an.
- `ThemeService` im Client wendet ein Theme zur Laufzeit an (`Apply`, `Preview`, `SetMode`).
