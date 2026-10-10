# Anmeldeseiten und Designs

Die Kontoseiten des Auth-Servers (Anmeldung, Registrierung, Bestätigung in zwei Schritten, vergessenes und neues Passwort, Zustimmung, Abmeldung, der App-Starter und die Sicherheitsseiten des Kontos) folgen dem Theme der App: Das Standard-Theme der Systemorganisation liefert die Farben für hellen und dunklen Modus, die Schrift, den Eckenradius und das Logo, die Konfiguration den App-Namen. Die Einstellung des Farbmodus (`Theme.Mode`: System, hell oder dunkel, die des Benutzers oder der Installation) gilt ebenfalls.

## Layouts

Drei Layouts kommen mit der Bibliothek:

| Layout | Aussehen |
|---|---|
| `Card` (Standard) | eine zentrierte Karte |
| `Split` | ganze Seite: Bild oder Video auf einer Seite, das Formular auf der anderen |
| `Slide` | eine Karte mit farbigem Feld neben dem Formular. Auf der Anmeldeseite lädt das Feld zur Registrierung ein (wenn sie erlaubt ist); auf der Registrierungsseite wechselt es auf die andere Seite und führt zurück zur Anmeldung. Browser mit dokumentübergreifenden View Transitions animieren den Wechsel. |

Die Seiten funktionieren ohne Skript. Ein kleines Skript ergänzt, was ein Browser allein nicht kann: einen Knopf, der das Passwort zeigt, einen Hinweis bei aktiver Feststelltaste, ein Abzeichen *Zuletzt verwendet* an der zuletzt gewählten Anmeldeart (nur wenn es externe Anbieter gibt) und ein einziges Absenden je Formular. Animationen entfallen, wenn das System reduzierte Bewegung verlangt.

## Konfiguration

```json
"Coworkee": {
  "Auth": {
    "Design": {
      "Layout": "Split",
      "MediaUrl": "/media/welcome.mp4",
      "MediaSide": "Right"
    },
    "AllowThemeDesign": true
  }
}
```

| Option | Bedeutung |
|---|---|
| `Design:Layout` | `Card`, `Split`, `Slide` oder der Name eines eigenen Layouts; unbekannte Namen fallen auf `Card` zurück |
| `Design:MediaUrl` | ein Bild, bei `.mp4`, `.webm`, `.ogg` und `.mov` ein Video; ein Pfad auf dem Auth-Server oder eine `http(s)`-Adresse, deren Origin in `img-src` und `media-src` der Content Security Policy aufgenommen wird |
| `Design:MediaSide` | `Left` (Standard) oder `Right`; bei `Slide` liegt das Feld auf der Registrierungsseite auf der anderen Seite |
| `AllowThemeDesign` | `true` (Standard) lässt Administratoren Layout und Medien je Theme wählen; `false` behält die Konfiguration |

## Im Theme-Editor

*Administration > Themes > Bearbeiten* zeigt über dem Theme-Editor einen Abschnitt *Anmeldeseite*: Layout, Adresse des Bildes oder Videos und Seite. Die Werte stehen in den Optionen des Themes unter `SignIn`; leere Felder behalten die Konfiguration. Es zählt das Standard-Theme der Systemorganisation. Die Client-Optionen steuern den Abschnitt:

```csharp
builder.Services.AddCoworkeeClient(options =>
{
    options.AllowThemeSignInDesign = false;   // blendet den Abschnitt aus
    options.SignInLayouts.Add("Aurora");      // bietet ein eigenes Layout an
});
```

Schalten Sie `AllowThemeSignInDesign` zusammen mit `Coworkee:Auth:AllowThemeDesign` auf dem Auth-Server: Der Client zeigt den Abschnitt, der Auth-Server wendet ihn an.

## Eigene Designs

Die Layouts sind Razor-Layouts neben den Seiten der Bibliothek: `Pages/_DesignCard.cshtml`, `Pages/_DesignSplit.cshtml` und `Pages/_DesignSlide.cshtml`, alle innerhalb von `Pages/_Layout.cshtml`, das Kopf, Theme-Variablen, Stylesheet und Skript rendert. Wie bei jeder Razor-Klassenbibliothek gewinnt eine View des Auth-Hosts unter demselben Pfad:

- **Eigenes Layout**: `Pages/_DesignAurora.cshtml` im Auth-Host anlegen und `Design:Layout` auf `Aurora` setzen (oder im Theme-Editor wählen).
- **Ein mitgeliefertes ersetzen**: `Pages/_DesignCard.cshtml` im Auth-Host anlegen.
- **Alles ersetzen**: `Pages/_Layout.cshtml` anlegen; dann gehören auch Stylesheet und Skript Ihnen.

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
