# Themes

Themes sind Daten: Paletten für hellen und dunklen Modus, Typografie, Eckenradius, Breite der Navigation und Höhe der App-Leiste, Schatten, Logo, eigenes CSS und die Optionen der App. Sieben Themes sind eingebaut (Ocean als Standard, Coworkee, Classic, Forest, Midnight, Sunset und High Contrast); sie unterscheiden sich in Schrift, Radius, Breite und Verhalten der Navigation, nicht nur in den Farben.

![Theme-Auswahl in den Benutzereinstellungen](../assets/screenshots/themes.png){ .shot }

- *Administration > Themes* (`Themes.Manage`) zeigt je Theme eine Kachel: helle und dunkle Farben, Name, Veröffentlichungsstatus, *Bearbeiten*, *Als Standard festlegen* und *Löschen* (nur eigene Themes, nach Rückfrage). *Bearbeiten* und *Neues Theme* öffnen einen `MudExThemeEdit<CoworkeeTheme>` mit Live-Vorschau; ein neues Theme startet von einem der vorhandenen. Eingebaute Themes bleiben unverändert, Speichern legt eine Kopie an.
- Alle Benutzer können die eingebauten und die *veröffentlichten* Themes wählen; der Standard des Mandanten gilt, bis ein Benutzer in den Kontoeinstellungen oder über die Palette in der App-Leiste ein anderes Theme (und hellen, dunklen oder System-Modus) wählt.
- `CoworkeeTheme` erbt von `MudTheme` und ergänzt: `ShowLogoInNav`, `ShowLogoInAppBar`, `ShowNavFilter`, `CanPinNav`, `CanChangeNavExpandMode`, `NavSingleExpand` (bis der Benutzer wählt), `Dense`, `StripedTables`, `IsPublished`, `LogoSvg`, `CustomCss`. Alle sind Felder im Editor. `CustomCss` ist ein Stylesheet für die ganze App (nach dem Theme gerendert) und wird in einem mitwachsenden mehrzeiligen Feld bearbeitet.
- `Dense` (standardmäßig an) macht die ganze Oberfläche kompakt: Tabellen, Listen, Menüs, Baumansichten, Hinweise, Ausklappbereiche und Checkboxen werden `Dense` dargestellt, Eingabefelder und Auswahllisten mit `Margin.Dense`, Chips mit `Size.Small`. Formulare (`MudExObjectEdit`) und das Navigationsmenü behalten ihre normale Größe. Themes, die mit der früheren Option `DenseTables` gespeichert wurden, behalten ihren Wert.
- Die Komponenten der Bibliothek erben von `CoworkeeComponentBase`, das die `Density` des Themes als Cascading Value erhält (`Density.Dense`, `Density.Margin`, `Density.Size`). Eigene Komponenten können das genauso nutzen: `@inherits CoworkeeComponentBase` oder `[CascadingParameter] Density Density { get; set; } = Density.Default;`. Eine eigene `IObjectMetaConfiguration<T>` eines Modells ersetzt die kompakte Formular-Konfiguration für dieses Modell.
- Ohne Logo im Theme erscheint das Logo der App: `options.AppLogo = "logo.svg"` in `AddCoworkeeClient`.
- Das Setup bietet das Theme als eigenen Schritt an.
- Die Kontoseiten des Auth-Servers übernehmen Farben, Schrift und Logo des Standard-Themes. Layout und Medien sind eine Einstellung für die ganze Installation: die Karte *Anmeldeseite* oben auf *Administration > Themes*, siehe [Anmeldeseiten und Designs](../security/sign-in-pages.md).
- Die Felder des Theme-Editors kommen aus MudEx-Metadaten für Objektformulare. Die Bibliothek registriert `CoworkeeThemeEditMeta` als `IObjectMetaConfiguration<CoworkeeTheme>` (das Feld `CustomCss`); registrieren Sie nach `AddCoworkeeClient` eine eigene, um Felder anders darzustellen (sie ersetzt die der Bibliothek):

```csharp
builder.Services.AddScoped<IObjectMetaConfiguration<CoworkeeTheme>, MyThemeEditMeta>();

public sealed class MyThemeEditMeta : IObjectMetaConfiguration<CoworkeeTheme>
{
    public Task ConfigureAsync(ObjectEditMeta<CoworkeeTheme> meta)
    {
        meta.Property(t => t.LogoSvg).WithLabel("Logo (SVG-Markup)");
        return Task.CompletedTask;
    }
}
```
- `ThemeService` im Client wendet ein Theme zur Laufzeit an (`Apply`, `Preview`, `SetMode`).

## Standard-Theme und Themes der App

`ThemingOptions` bestimmt das Standard-Theme der Installation und ergänzt Themes der App. Ein App-Theme ist ein eingebautes Theme mit anderen Farben; es wird wie die eingebauten angelegt und gepflegt (in der App schreibgeschützt, im Setup angeboten). Ein geändertes `Primary` oder `Secondary` bekommt eine lesbare Kontrastfarbe für Text, sofern die Palette keine nennt.

```csharp title="MyApp.Infrastructure/MyAppInfrastructureModule.cs"
context.Services.Configure<ThemingOptions>(themes =>
{
    themes.DefaultTheme = "Shop";
    themes.Themes.Add(new AppTheme("Shop", "Ocean", new Dictionary<string, string> { ["Primary"] = "#0a7d5a", ["Secondary"] = "#ff8f00" }));
});
```

Ein unbekannter Name in `DefaultTheme` lässt Ocean als Standard. Der Standard gilt, bis eine Organisation ein eigenes Theme wählt (Setup oder *Als Standard*). `coworkee new --theme Forest --primary-color "#0a7d5a"` schreibt das für Sie.
