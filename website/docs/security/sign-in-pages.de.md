# Anmeldeseiten und Designs

Die Kontoseiten des Auth-Servers (Anmeldung, Registrierung, Bestätigung in zwei Schritten, vergessenes und neues Passwort, Zustimmung, Abmeldung, der App-Starter und die Sicherheitsseiten des Kontos) folgen dem Theme der App: Das Standard-Theme der Systemorganisation liefert die Farben für hellen und dunklen Modus, die Schrift, den Eckenradius und das Logo, die Konfiguration den App-Namen. Die Einstellung des Farbmodus (`Theme.Mode`: System, hell oder dunkel, die des Benutzers oder der Installation) gilt ebenfalls.

## Layouts

Drei Layouts kommen mit der Bibliothek:

| Layout | Aussehen |
|---|---|
| `Card` | eine zentrierte Karte |
| `Split` | ganze Seite: Bild oder Video auf einer Seite, das Formular auf der anderen |
| `Slide` (Standard) | eine Karte mit farbigem Feld neben dem Formular. Auf der Anmeldeseite lädt das Feld zur Registrierung ein (wenn sie erlaubt ist); auf der Registrierungsseite wechselt es auf die andere Seite und führt zurück zur Anmeldung. Browser mit dokumentübergreifenden View Transitions animieren den Wechsel. |

Die Seiten funktionieren ohne Skript. Ein kleines Skript ergänzt, was ein Browser allein nicht kann: einen Knopf, der das Passwort zeigt, einen Hinweis bei aktiver Feststelltaste, ein Abzeichen *Zuletzt verwendet* an der zuletzt gewählten Anmeldeart (nur wenn es externe Anbieter gibt) und ein einziges Absenden je Formular. Passkeys brauchen ein zweites kleines Skript, `passkey.js`; ohne es bleibt ihr Button verborgen (siehe [Passkeys](passkeys.md)). Animationen entfallen und Videos stehen still, wenn das System reduzierte Bewegung verlangt.

**Seitenhintergrund.** Unabhängig vom Bild oder Video des Layouts legt `Design:BackgroundUrl` ein Bild oder Video hinter die ganze Seite (füllend, fest beim Scrollen), auf Wunsch abgedunkelt mit `Design:BackgroundDim`. `Card` und `Slide` zeigen ihn um ihre Karte herum; `Split` zeigt ihn hinter der Formularseite, wo das Formular dann auf einer eigenen Karte liegt und lesbar bleibt; die Medienseite behält ihr Bild oder Video. Auf Telefonen füllen `Card` und `Slide` den Bildschirm, dort bleibt der Hintergrund verdeckt. Eigene Layouts bekommen ihn ebenfalls, denn `_Layout.cshtml` rendert ihn. Ohne den Wert sehen die Seiten aus wie bisher.

## Konfiguration

Das Design gilt für die ganze Installation, unabhängig davon, welches Theme Standard ist. Die Konfiguration legt den Ausgangspunkt fest:

```json
"Coworkee": {
  "Auth": {
    "Design": {
      "Layout": "Split",
      "MediaUrl": "/media/welcome.mp4",
      "MediaSide": "Right",
      "BackgroundUrl": "/media/sky.jpg",
      "BackgroundDim": 30
    },
    "AllowDesignChanges": true
  }
}
```

| Option | Bedeutung |
|---|---|
| `Design:Layout` | `Card`, `Split`, `Slide` oder der Name eines eigenen Layouts; unbekannte Namen fallen auf `Slide` zurück |
| `Design:MediaUrl` | ein Bild, bei `.mp4`, `.webm`, `.ogg`, `.ogv` und `.mov` ein Video; ein Pfad auf dem Auth-Server oder eine `http(s)`-Adresse, deren Origin in `img-src` und `media-src` der Content Security Policy aufgenommen wird |
| `Design:MediaSide` | `Left` (Standard) oder `Right`; bei `Slide` liegt das Feld auf der Registrierungsseite auf der anderen Seite |
| `Design:BackgroundUrl` | ein Bild oder Video hinter der ganzen Seite in jedem Layout, mit denselben Regeln wie `MediaUrl` (Pfad oder `http(s)`-Adresse, deren Origin in die Policy kommt); leer behält den Hintergrund des Themes |
| `Design:BackgroundDim` | dunkelt den Hintergrund ab, `0` bis `90` Prozent (Standard `0`) |
| `Design:LoadingScreen` | Hintergrund des [Ladebildschirms](../modules/theming.md#ladebildschirm) der App: `Animated`, `SignInMedia` (das Bild oder Video oben) oder `Plain` |
| `AllowDesignChanges` | `true` (Standard) lässt Administratoren das Design auf der Themes-Seite ändern; `false` behält die Konfiguration. Setz es für den Dienst, der die API hostet: Dort wird die Änderung gespeichert. |

Der Aspire-AppHost setzt dieselben Werte mit `options.SignIn` und liefert einen Pfad zum Medium von der Web-App aus, siehe [Aspire-AppHost](../hosting/aspire.md).

## Auf der Themes-Seite

*Administration > Themes* beginnt mit der Karte *Anmeldeseite*: eine Vorschau je Layout zum Auswählen, die Adresse des Bildes oder Videos und seine Seite, der Seitenhintergrund mit seiner Abdunklung sowie der Hintergrund des [Ladebildschirms](../modules/theming.md#ladebildschirm) der App. Die Ordner-Knöpfe in den Adressfeldern wählen das Bild oder Video aus den Dateien der App; eine private Datei wird vorher (nach Rückfrage) öffentlich gemacht, und das Feld erhält ihre absolute öffentliche Adresse, die Auth-Server, Keycloak und Ladebildschirm ohne Anmeldung laden können. *Speichern* legt die Werte als App-Konfigurationsabschnitt `Coworkee:Auth:Design` ab (Tabelle `cw.ConfigurationEntries`, siehe [App-Konfiguration](../modules/settings.md)); *Standardwerte wiederherstellen* verwirft sie, dann gilt wieder die Konfiguration.

*Vorschau* öffnet die echte Anmeldeseite des Auth-Servers mit den Werten, die die Karte gerade zeigt, gespeichert oder nicht, in einem Vollbild-Dialog. Der Dialog rahmt `/bff/sign-in-preview?design={json}` ein, das den Rahmen an `/Account/Preview` des Auth-Servers weiterleitet; diese Adresse rendert die Anmeldeseite mit dem übergebenen Design nach denselben Prüfungen wie ein gespeichertes. Sie antwortet nur auf ein `GET`, das der Browser als Laden eines Rahmens kennzeichnet (`Sec-Fetch-Dest: iframe`), und nur die Origins registrierter Clients dürfen sie einrahmen (`frame-ancestors`); ein Link oder eine andere Seite kann so kein untergeschobenes Design unter der Adresse des Auth-Servers zeigen. Der Rahmen läuft ohne Formulare (`sandbox`), dort meldet sich niemand an, und die Sitzung des Administrators bleibt unberührt. Die Policy der Web-App erlaubt dafür die Origin des Auth-Servers in `frame-src`. Mit Keycloak als Anmeldung zeigt die Vorschau die Seite des Coworkee-Auth-Servers, die es dort nicht gibt; dort übernimmt die Keycloak-Karte das Design.

Die Karte sehen Administratoren der Systemorganisation, die Einstellungen verwalten dürfen (`Settings.Manage`), denn der Abschnitt gilt für alle Organisationen. Für alle anderen und solange `AllowDesignChanges` aus ist, bleibt sie verborgen.

**Wie schnell der Auth-Server folgt.** Der Auth-Server liest das Design bei jeder Anfrage aus seiner Konfiguration. Eine Änderung sieht er, sobald seine Datenbank-Konfigurationsquelle neu lädt: `AddCoworkeeDatabaseConfiguration` prüft standardmäßig alle 15 Sekunden (der Auth-Host der Vorlage fügt sie hinzu). Ein Host im selben Prozess wie die API sieht die Änderung sofort. Farben, Schrift und Logo kommen aus dem Standard-Theme und werden eine Minute zwischengespeichert.

Die Client-Optionen steuern die Karte:

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.AllowSignInDesign = false;        // blendet die Karte aus
    options.SignInLayouts.Add("Aurora");      // bietet ein eigenes Layout an
});
```

Ein eigenes Layout erhält eine allgemeine Vorschau mit seinem Namen. Themes, die noch den früheren Wert je Theme tragen (`SignIn` in den Theme-Optionen), werden ignoriert; leg das Design einmal auf der Themes-Seite fest.

## Eigene Designs

Die Layouts sind Razor-Layouts neben den Seiten der Bibliothek: `Pages/_DesignCard.cshtml`, `Pages/_DesignSplit.cshtml` und `Pages/_DesignSlide.cshtml`, alle innerhalb von `Pages/_Layout.cshtml`, das Kopf, Theme-Variablen, Stylesheet und Skript rendert. Wie bei jeder Razor-Klassenbibliothek gewinnt eine View des Auth-Hosts unter demselben Pfad:

- **Eigenes Layout**: `Pages/_DesignAurora.cshtml` im Auth-Host anlegen und `Design:Layout` auf `Aurora` setzen (oder auf der Themes-Seite wählen).
- **Ein mitgeliefertes ersetzen**: `Pages/_DesignCard.cshtml` im Auth-Host anlegen.
- **Alles ersetzen**: `Pages/_Layout.cshtml` anlegen; dann gehören auch Stylesheet und Skript dir.

```cshtml
@* Pages/_DesignAurora.cshtml im Auth-Host *@
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@inject Coworkee.AuthServer.AuthBrandingProvider Branding
@{
  Layout = "_Layout";
  ViewData["Design"] = "Aurora";
  var brand = await Branding.GetAsync(Context.RequestAborted);
}
<div class="aurora">
  <main class="panel">
    <partial name="_Brand" model="brand" />
    @RenderBody()
  </main>
</div>
<style>
  body.layout-aurora { display: grid; place-items: center; background: radial-gradient(circle at top, var(--signal), var(--paper) 60%); }
  .aurora .panel { padding: 40px; background: var(--surface); border-radius: calc(var(--radius) * 3); }
</style>
```

`AuthBranding` enthält App-Namen, Logo, das CSS des Themes und das aufgelöste `Design`; die Partial-View `_Media` rendert dessen Bild oder Video. Das Seitenmodell ist `ViewContext.ViewData.Model` (`LoginModel`, `RegisterModel`, ...), so kann ein Layout die Seiten unterscheiden. Das Stylesheet definiert diese Variablen, die das Theme füllt: `--paper`, `--surface`, `--ink`, `--ink-2`, `--rule`, `--signal`, `--on-signal`, `--accent`, `--error`, `--success`, `--radius` und `--font`. Die Content Security Policy erlaubt Inline-Styles, Skripte aber nur vom Auth-Server selbst.
