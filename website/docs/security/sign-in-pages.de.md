# Anmeldeseiten und Designs

Die Kontoseiten des Auth-Servers (Anmeldung, Registrierung, Bestätigung in zwei Schritten, vergessenes und neues Passwort, Zustimmung, Abmeldung, der App-Starter und die Sicherheitsseiten des Kontos) folgen dem Theme der App: Das zuletzt im Browser verwendete Theme, sonst das Standard-Theme der Systemorganisation, liefert die Farben für hellen und dunklen Modus, die Schrift, den Eckenradius, das Logo und das Anmeldedesign, das es nennt (siehe [Designs je Theme](#designs-je-theme)); die Konfiguration liefert den App-Namen. Die Einstellung des Farbmodus (`Theme.Mode`: System, hell oder dunkel, die des Benutzers oder der Installation) gilt ebenfalls.

## Layouts

Sieben Layouts kommen mit der Bibliothek, jedes mit eigenem Aussehen:

| Layout | Aussehen |
|---|---|
| `Card` | eine zentrierte Karte. *Oberfläche* `Glass` macht sie zu Milchglas (durchscheinend, unscharf, feiner Rand und Schatten; ohne Hintergrund über einem sanften Verlauf aus Primär- und Tertiärfarbe des Themes; Browser ohne `backdrop-filter` zeigen die normale Fläche). *Animation* `Aurora` lässt weiche Farbfelder in den Farben der Seitenfüllung (sonst Primär-, Sekundär- und Tertiärfarbe des Themes) dahinter wandern. |
| `Split` | ganze Seite: Bild, Video oder Füllung auf einer Seite, das Formular auf der anderen |
| `Slide` (Standard) | eine Karte mit farbigem Feld neben dem Formular. Auf der Anmeldeseite lädt das Feld zur Registrierung ein (wenn sie erlaubt ist); auf der Registrierungsseite wechselt es auf die andere Seite und führt zurück zur Anmeldung. Browser mit dokumentübergreifenden View Transitions animieren den Wechsel. |
| `Spotlight` | der Seitenhintergrund füllt den ganzen Bildschirm, der App-Name steht groß darauf, und das Formular schwebt in einem Panel voller Höhe am linken oder rechten Rand (24 px Abstand, abgerundet, deckend oder Glas), das hereingleitet. Auf Telefonen wird das Panel zum Bottom Sheet unter dem offenen Hintergrund. |
| `Poster` | zwei Spalten ohne Karte: Die Posterseite trägt den App-Namen in riesigen Buchstaben, Titel und Untertitel der Anmeldeseite aus dem Standarddesign als Claim und auf Wunsch ein Bild oder Video; die Formularseite bleibt ruhig mit Linien-Eingabefeldern und runden Knöpfen. |
| `Stack` | die Formularkarte vor zwei geneigten Karten (die zweite versetzt), die die Panel-Füllung oder das Bild zeigen; zwischen Anmeldung und Registrierung dreht sich der Stapel (View Transition) und die Karten neigen sich zur anderen Seite. *Neigung* setzt den Winkel, 0 bis 12 Grad (Standard 6). |
| `Minimal` | keine Karte, kein Rand: ein großes Logo (*Logogröße* klein, mittel oder groß) und das Formular direkt auf dem Seitenhintergrund; über einem Bild oder Video bekommt das Formular einen sanften Milchglas-Untergrund, damit es lesbar bleibt. |

Alle sieben funktionieren im hellen und dunklen Modus, mit Registrierung, Passkeys, externen Anbietern, den Texten und ohne App-Namen. Auf Telefonen füllen `Card` und `Slide` den Bildschirm wie eine App; eine Karte über einem Hintergrund, mit Glas oder Aurora schwebt weiter mit kleinem Rand, denn der Hintergrund ist der Sinn der Sache. Alle Einblend-Animationen, die wandernden Felder, die verlaufenden Füllungen und das Drehen entfallen, wenn das System reduzierte Bewegung verlangt.

Die Seiten funktionieren ohne Skript. Ein kleines Skript ergänzt, was ein Browser allein nicht kann: einen Knopf, der das Passwort zeigt, einen Hinweis bei aktiver Feststelltaste, ein Abzeichen *Zuletzt verwendet* an der zuletzt gewählten Anmeldeart (nur wenn es externe Anbieter gibt) und ein einziges Absenden je Formular. Passkeys brauchen ein zweites kleines Skript, `passkey.js`; ohne es bleibt ihr Button verborgen (siehe [Passkeys](passkeys.md)). Animationen entfallen und Videos stehen still, wenn das System reduzierte Bewegung verlangt.

### Optionen je Layout

Jedes Layout zeigt nur die Optionen, die für es Sinn ergeben, gleich in der Admin-Karte, im Studio und hier (`SignInLayoutOptions.For(layout)` in `Coworkee.Contracts.Theming`):

| Layout | Hintergrund 1 | Hintergrund 2 | Seite | Oberfläche | Animation | Abdunkeln | Neigung | Logogröße |
|---|---|---|---|---|---|---|---|---|
| `Card` | Seitenhintergrund | - | - | ja | ja | ja | - | - |
| `Split` | Formularseite | Medienseite | Seite des Bildes | - | - | - | - | - |
| `Slide` | Seitenhintergrund | Panel | Seite des Panels | - | - | ja | - | - |
| `Spotlight` | Seitenhintergrund | - | Seite des Panels | ja | ja | ja | - | - |
| `Poster` | Formularseite | Poster | Seite des Plakats | - | - | - | - | - |
| `Stack` | Seitenhintergrund | Karten dahinter | - | - | ja | ja | ja | - |
| `Minimal` | Seitenhintergrund | - | - | - | ja | ja | - | ja |
| eigenes Layout | Seitenhintergrund | Panel | Seite des Bildes | ja | ja | ja | - | - |

Die Seiten lassen weg, was ein Layout nicht zeigt; ein für ein anderes Layout gespeicherter Wert schadet also nicht.

### Hintergründe: Dateien, Farben und Verläufe

Jeder Hintergrund ist eines von vier Dingen: der des Themes (nichts gesetzt), ein Bild oder Video (Hintergrund 1: `BackgroundUrl`, Hintergrund 2: `MediaUrl`), eine Farbe oder ein Verlauf (`PageFill`, `PanelFill`, gelten, wenn die Datei desselben Hintergrunds leer ist). Eine Füllung ist entweder `Color` oder `Gradient`:

```json
"PageFill": { "Color": "#0f2027" },
"PanelFill": {
  "Gradient": {
    "Kind": "Linear",
    "Angle": 135,
    "Animated": true,
    "Stops": [
      { "Color": "primary", "Position": 0 },
      { "Color": "#ffc371", "Position": 100 }
    ]
  }
}
```

- `Kind`: `Linear`, `Radial` oder `Conic`; `Angle` 0 bis 359 Grad (Richtung eines linearen Verlaufs, Beginn eines konischen); `Animated` verschiebt den Verlauf langsam.
- Bis zu 8 Farbpunkte mit `Position` 0 bis 100.
- Farben: `#rgb`, `#rrggbb`, `#rrggbbaa`, `rgb()`, `rgba()`, `hsl()` oder `hsla()` nur mit Zahlen, oder eine Theme-Farbe `primary`, `secondary`, `tertiary`, `background` oder `surface`, die dem Theme folgt (hell, dunkel, ein anderes Theme).

Füllungen landen in den Styles der Seite, deshalb bauen Auth-Server, Vorschau und Keycloak-Theme sie nur aus geprüften Teilen neu zusammen (`SignInFills`): Jede andere Farbe fällt weg, Zahlen werden begrenzt, überzählige Farbpunkte ignoriert, ein Verlauf mit nur einem gültigen Punkt wird zu dieser Farbe. Text direkt auf einer Füllung (das minimale Formular, die Formularseite des Posters, der Schriftzug von Spotlight, Panels) wird hell oder dunkel je nach Helligkeit der Füllfarben.

Auf der Seite *Anmeldung* hat jeder Hintergrund eine segmentierte Auswahl *Vom Theme*, *Bild oder Video*, *Farbe* und *Verlauf*. Die Verlaufssteuerung zeigt eine große Live-Vorschau und die Farbpunkte als Griffe auf einer Leiste: Ein Klick auf die Leiste fügt einen Punkt mit der Farbe an dieser Stelle hinzu, ein Griff lässt sich ziehen (oder mit den Pfeiltasten bewegen, Umschalt für Zehnerschritte), *Entf* löscht ihn (zwei bleiben); der gewählte Punkt hat ein Farbfeld, die Theme-Farben und seine Position; Art, Winkel (mit Drehanzeige), *Animiert* und einige Vorlagen ergänzen sie.

**Frühere Layouts Glass und Aurora.** Sie sind jetzt Optionen von `Card`: Ein gespeichertes Design mit `Layout` `Glass` gilt als `Card` mit `Surface` `Glass` (sein Bild oder Video wird zum Seitenhintergrund, wenn es keinen hat), `Aurora` als `Card` mit `Animation` `Aurora`; Auth-Server, Vorschau, Keycloak-Theme und Admin-Karte lesen es so, und Speichern der Seite legt die neue Form ab. Die Konstanten `SignInLayouts.Glass` und `SignInLayouts.Aurora` bleiben, als veraltet markiert. Eine eigene Ansicht `Pages/_DesignGlass.cshtml` oder `_DesignAurora.cshtml` des Auth-Hosts gewinnt weiterhin.

**Seitenhintergrund.** Unabhängig von Hintergrund 2 legt `Design:BackgroundUrl` (oder `PageFill`) ein Bild, Video oder eine Füllung hinter die ganze Seite (füllend, fest beim Scrollen), auf Wunsch abgedunkelt mit `Design:BackgroundDim`, wo das Layout es anbietet. `Card`, `Slide`, `Stack` und `Spotlight` zeigen ihn um ihr Formular herum; `Split` und `Poster` zeigen ihn hinter der Formularseite (`Split` legt das Formular dann auf eine eigene Karte, damit es lesbar bleibt). Auf Telefonen füllen `Card` und `Slide` ohne Hintergrund den Bildschirm. Eigene Layouts bekommen ihn ebenfalls, denn `_Layout.cshtml` rendert ihn. Ohne den Wert sehen die Seiten aus wie bisher.

## Konfiguration

Das Design gilt für die ganze Installation, außer ein Theme nennt eines der benannten Designs (siehe [Designs je Theme](#designs-je-theme)). Die Konfiguration legt den Ausgangspunkt fest:

```json
"Coworkee": {
  "Auth": {
    "Design": {
      "Layout": "Split",
      "MediaUrl": "/media/welcome.mp4",
      "MediaSide": "Right",
      "BackgroundUrl": "/media/sky.jpg",
      "BackgroundDim": 30,
      "ShowAppName": true,
      "Texts": {
        "Title": "Welcome",
        "Titles": { "de": "Willkommen" },
        "Notice": "Wartung am Sonntag von 20 bis 22 Uhr."
      }
    },
    "AllowDesignChanges": true
  }
}
```

| Option | Bedeutung |
|---|---|
| `Design:Layout` | `Card`, `Split`, `Slide`, `Spotlight`, `Poster`, `Stack`, `Minimal` oder der Name eines eigenen Layouts; unbekannte Namen fallen auf `Slide` zurück, die früheren `Glass` und `Aurora` gelten als `Card` (siehe oben) |
| `Design:MediaUrl` | ein Bild, bei `.mp4`, `.webm`, `.ogg`, `.ogv` und `.mov` ein Video; ein Pfad auf dem Auth-Server oder eine `http(s)`-Adresse, deren Origin in `img-src` und `media-src` der Content Security Policy aufgenommen wird |
| `Design:MediaSide` | `Left` oder `Right`: die Bildseite von `Split` und die Posterseite von `Poster` (Standard links), die Panelseite von `Slide` und `Spotlight` (Standard rechts); bei `Slide` liegt das Feld auf der Registrierungsseite auf der anderen Seite |
| `Design:BackgroundUrl` | ein Bild oder Video hinter der ganzen Seite in jedem Layout, mit denselben Regeln wie `MediaUrl` (Pfad oder `http(s)`-Adresse, deren Origin in die Policy kommt); leer behält den Hintergrund des Themes |
| `Design:BackgroundDim` | dunkelt Hintergrund 1 ab, `0` bis `90` Prozent (Standard `0`), in den Layouts, die es anbieten |
| `Design:PageFill`, `Design:PanelFill` | Hintergrund 1 und 2 als Farbe oder Verlauf, gelten, wenn `BackgroundUrl` bzw. `MediaUrl` leer ist; siehe [Hintergründe](#hintergrunde-dateien-farben-und-verlaufe) |
| `Design:Surface` | `Solid` (Standard) oder `Glass` für `Card` und `Spotlight` |
| `Design:Animation` | `None` (Standard) oder `Aurora`: wandernde Farbfelder hinter dem Formular |
| `Design:Tilt` | Neigung der Karten hinter dem Formular bei `Stack`, `0` bis `12` Grad (Standard `6`) |
| `Design:LogoSize` | `Small`, `Medium` (Standard) oder `Large` bei `Minimal` |
| `Design:LoadingScreen` | Hintergrund des [Ladebildschirms](../modules/theming.md#ladebildschirm) der App: `Animated`, `SignInMedia` (das Bild oder Video oben) oder `Plain` |
| `Design:ShowAppName` | `true` (Standard) zeigt den App-Namen neben dem Logo und auf der Medienfläche von `Split` und `Slide`; `false` lässt das Logo allein stehen |
| `Design:Texts:Title`, `Subtitle`, `Notice` | eigene Überschrift, eine Zeile darunter und ein Hinweis über dem Formular der Anmeldeseite, für jede Sprache; leer behält die eingebaute Überschrift *Anmelden* und zeigt weder Zeile noch Hinweis |
| `Design:Texts:Titles`, `Subtitles`, `Notices` | dasselbe je Sprache (`"de"`, `"de-AT"`): die Sprache des Besuchers (`de-AT`, dann `de`) geht dem Text für jede Sprache vor |
| `Design:Designs` | weitere, benannte Designs, die Themes wählen können: jedes mit `Key`, `Name` und den Werten des Aussehens `Layout`, `MediaUrl`, `MediaSide`, `BackgroundUrl`, `BackgroundDim`, `PageFill`, `PanelFill`, `Surface`, `Animation`, `Tilt`, `LogoSize`; siehe [Designs je Theme](#designs-je-theme) |
| `AllowDesignChanges` | `true` (Standard) lässt Administratoren das Design auf der Seite *Anmeldung* ändern; `false` behält die Konfiguration. Setz es für den Dienst, der die API hostet: Dort wird die Änderung gespeichert. |

Der Aspire-AppHost setzt dieselben Werte mit `options.SignIn` und liefert einen Pfad zum Medium von der Web-App aus, siehe [Aspire-AppHost](../hosting/aspire.md).

## Designs je Theme

Die Werte oben sind das **Standarddesign**. `Design:Designs` ergänzt benannte Designs, und ein Theme nennt das, das es möchte: im Theme-Editor (*Anmeldedesign*, Standard *Standarddesign*) oder in den Optionen des Themes als `SignInDesign` mit dem Schlüssel des Designs. Ein benanntes Design bringt sein eigenes Aussehen mit (Layout, beide Hintergründe, Seite und die Optionen des Layouts); Texte, *App-Namen anzeigen* und Ladebildschirm bleiben die des Standarddesigns, ein Hinweis erreicht also alle, egal welches Theme sie nutzen.

```json
"Coworkee": {
  "Auth": {
    "Design": {
      "Layout": "Slide",
      "Designs": [
        { "Key": "frost", "Name": "Frost", "Layout": "Card", "Surface": "Glass", "BackgroundUrl": "/media/sea.mp4" },
        { "Key": "night", "Name": "Nacht", "Layout": "Spotlight", "PageFill": { "Gradient": { "Kind": "Radial", "Stops": [ { "Color": "#845ec2", "Position": 0 }, { "Color": "#1b1035", "Position": 80 } ] } } }
      ]
    }
  }
}
```

```csharp
// ein Theme der App, das das Design "night" zeigt
builder.Services.Configure<ThemingOptions>(o => o.Themes.Add(new AppTheme("Night", "Ocean", new Dictionary<string, string> { ["Primary"] = "#5e35b1" })
{
    Options = new Dictionary<string, object> { [SignInDesign.ThemeOption] = "night" },
}));
```

Welches Design eine Anmeldeseite zeigt:

1. das Design, das das zuletzt in diesem Browser verwendete Theme nennt: Die Web-App merkt sich das angewendete Theme in ihrem Cookie `coworkee-loading` und nennt es in der Autorisierungsanfrage (`coworkee_theme`); der Auth-Server hält es in seinem eigenen Cookie `coworkee-theme` (HttpOnly, SameSite Lax, ein Jahr) für jede Kontoseite dieses Browsers. Nur ein mitgeliefertes oder veröffentlichtes Theme der Systemorganisation zählt; jede andere Id, oder keine, fällt durch. Farben, Schrift und Logo dieses Themes gelten dann ebenfalls.
2. sonst das Design, das das Standard-Theme der Systemorganisation nennt,
3. sonst das Standarddesign; auch wenn ein Theme ein Design nennt, das es nicht mehr gibt.

Die Keycloak-Anmeldeseite kann das Theme des Browsers nicht kennen: Sie zeigt das Design, das das Standard-Theme nennt. Eine bestehende Konfiguration ohne `Designs` sieht genau aus wie bisher; nichts muss geändert werden.

## Texte

Die Texte ändern nur die Anmeldeseite (`/Account/Login`); die anderen Kontoseiten behalten ihre Überschriften. Sie sind reiner Text: Die Seite kodiert sie, Markup erscheint also so, wie es eingegeben wurde, und kann nichts einschleusen; ein Zeilenumbruch im Hinweis bleibt erhalten. Die Sprache ist die, die der Auth-Server für die Anfrage wählt (die Sprache des Browsers oder `?ui-culture=de`). Das Keycloak-Login-Theme bekommt die Texte ebenfalls: Sein Skript setzt sie als Text auf die Anmeldeseite von Keycloak, in deren Sprache; ohne App-Namen lässt das Theme den Kopftext von Keycloak leer. Ein gespeichertes Design ohne die neuen Werte sieht aus wie bisher.

## Auf der Seite Anmeldung

*Administration > System > Anmeldung* (`/cw/admin/sign-in`, Seitenschlüssel `sign-in`) bündelt die ganze Anmelde- und Registrierungskonfiguration in Tabs, eine Karte je Thema. Sie ist nur eine weitere Ansicht auf dieselben Quellen; nichts wird anders gespeichert, und *Einstellungen* zeigt und ändert weiterhin jeden Wert:

| Tab | Bedienelemente | Quelle |
|---|---|---|
| *Methoden* | *Passwort*, *Externer Anbieter* oder *Passwort und externe Anbieter* als Segmentauswahl; der Schalter *Passkeys* | Einstellungen `Auth.LoginMode`, `Account.Passkeys.Enabled` |
| *Sicherheit* | Passwortregeln (Länge und verschiedene Zeichen in Zeichen, Ablauf in Tagen, Verlauf in Passwörtern, vier Schalter), Sperre (Versuche, Minuten), Sitzungen (Sitzungen je Benutzer, Verhalten beim Limit), *Weitere Einstellungen* | die Einstellungsgruppen *Anmeldesicherheit* und *Konten* |
| *Registrierung* | Schalter für Selbstregistrierung, Aktivierung, E-Mail-Bestätigung und Adresse; erlaubte E-Mails als Chips; die Dokumente als Liste (Name, Hinweis, Pflicht, erlaubte Dateitypen, größte Datei, Übersetzungen) | die Einstellungen `Account.AllowRegistration`, `Account.RegistrationRequiresActivation`, `Account.RegistrationRequiresEmailConfirmation` und der Teil `Registration` der App-Einstellungen |
| *Darstellung* | die Karte *Anmeldedesigns* und darunter die Karte des gewählten Designs, beide unten beschrieben | der App-Konfigurationsabschnitt `Coworkee:Auth:Design` |
| *Texte* | *App-Namen anzeigen*; Titel, Untertitel und Hinweis für alle Sprachen und, nach Wahl einer Sprache, für diese Sprache (der Text für alle Sprachen erscheint als Platzhalter) | `ShowAppName` und `Texts` desselben Abschnitts |

Eine Speicherleiste in der klebenden Tab-Leiste speichert alle Tabs: Sie zeigt *Ungespeicherte Änderungen*, solange etwas vom Geladenen abweicht, *Verwerfen* stellt es wieder her, *Speichern* sendet nur, was sich geändert hat, in derselben Form wie die allgemeinen Seiten: die geänderten Einstellungen in einem Aufruf, die App-Einstellungen mit ersetztem Teil `Registration` und allen übrigen Teilen, wie sie kamen, das Design als Ganzes. Ein fehlschlagender Teil meldet seinen Fehler und behält seine Änderungen; die anderen werden gespeichert.

Die Seite ordnet die Einstellungen an, die sie kennt; jede andere Einstellung der beiden Gruppen (eine eigene der App, *Handeln in einer Rolle*) erscheint unter *Weitere Einstellungen* mit einem Bedienelement ihres Typs (Schalter, Zahl, Auswahl, Text): der Komponente `SettingField`, die auch Seiten einer App nutzen können. Eine Einstellung, die die App nicht definiert, erscheint nicht: Ohne externen Anbieter gibt es die Einstellung *Anmeldemethoden* nicht, und *Methoden* sagt, dass sich Benutzer mit ihrem Passwort anmelden. Legt der Betreiber den Modus mit `Coworkee:Auth:External:ModeOverride` fest, erscheint der festgelegte Modus ausgewählt und schreibgeschützt mit einem Hinweis (setz die Variable auch für die API, die die Einstellungen ausliefert). Gesperrte oder ausgeblendete Teile der App-Einstellungen erscheinen schreibgeschützt (*In der Konfiguration festgelegt*) oder gar nicht. Die Dokumente brauchen einen Dokumentenspeicher (das Files-Modul oder einen eigenen der App); solange die App keine Dateiberechtigung definiert, ist die Liste schreibgeschützt mit einem Hinweis.

Die Seite braucht `Settings.Manage`; das Menü zeigt sie der Systemorganisation. Andere Organisationen sehen nur den Tab *Sicherheit* mit ihren eigenen Sitzungseinstellungen und keine App-Konfiguration. Die Seite folgt Änderungen anderer sofort: Eine gespeicherte Einstellung oder ein gespeicherter Konfigurationswert lädt seinen Teil neu, außer dieser Teil hat ungespeicherte Änderungen. *Administration > Themes* verlinkt auf die Seite.

Die Karte *Anmeldedesigns* zeigt das Standarddesign und die benannten als Kacheln: eine Vorschau des Layouts in den Farben des aktuellen Themes, den Namen, das Layout und die Themes, die es zeigen (beim Standarddesign: die Themes, die keins nennen oder eines, das es nicht mehr gibt). *Design hinzufügen* beginnt ein benanntes Design (eine Glaskarte), *Duplizieren* kopiert das Aussehen des gewählten Designs unter neuem Namen und Schlüssel, das Feld *Name* benennt es um (der Schlüssel, den Themes speichern, bleibt), *Löschen* fragt vorher nach und nennt die Themes, die das Design verwenden und danach das Standarddesign zeigen. Die Seite folgt Theme-Änderungen anderer sofort. Wie jede andere Änderung auf der Seite warten diese auf die Speicherleiste.

Die Karte darunter bearbeitet das gewählte Design. Sie zeigt eine Vorschau je Layout zum Auswählen (in den Füllungen des Designs gezeichnet), dann nur, was das gewählte Layout anbietet (siehe [Optionen je Layout](#optionen-je-layout)): seinen einen oder seine zwei Hintergründe, jeden mit *Vom Theme*, *Bild oder Video*, *Farbe* und *Verlauf*, die Seite, Oberfläche, Aurora, Abdunklung, Neigung oder Logogröße; beim Standarddesign außerdem den Hintergrund des [Ladebildschirms](../modules/theming.md#ladebildschirm) der App (und dessen Bild oder Video, wenn das Layout keinen zweiten Hintergrund hat). Die Ordner-Knöpfe in den Adressfeldern wählen das Bild oder Video aus den Dateien der App; eine private Datei wird vorher (nach Rückfrage) öffentlich gemacht, und das Feld erhält ihre absolute öffentliche Adresse, die Auth-Server, Keycloak und Ladebildschirm ohne Anmeldung laden können. Die Speicherleiste legt die Werte als App-Konfigurationsabschnitt `Coworkee:Auth:Design` ab (Tabelle `cw.ConfigurationEntries`, siehe [App-Konfiguration](../modules/settings.md)); *Standardwerte wiederherstellen* verwirft sie nach Rückfrage, dann gilt wieder die Konfiguration.

*Vorschau* öffnet die echte Anmeldeseite des Auth-Servers mit den Werten, die die Karte gerade zeigt, gespeichert oder nicht, in einem Vollbild-Dialog; bei einem benannten Design mit den Texten des Standarddesigns. Der Dialog rahmt `/bff/sign-in-preview?design={json}` ein, das den Rahmen an `/Account/Preview` des Auth-Servers weiterleitet; diese Adresse rendert die Anmeldeseite mit dem übergebenen Design nach denselben Prüfungen wie ein gespeichertes. Sie antwortet nur auf ein `GET`, das der Browser als Laden eines Rahmens kennzeichnet (`Sec-Fetch-Dest: iframe`), und nur die Origins registrierter Clients dürfen sie einrahmen (`frame-ancestors`); ein Link oder eine andere Seite kann so kein untergeschobenes Design unter der Adresse des Auth-Servers zeigen. Der Rahmen läuft ohne Formulare (`sandbox`), dort meldet sich niemand an, und die Sitzung des Administrators bleibt unberührt. Die Policy der Web-App erlaubt dafür die Origin des Auth-Servers in `frame-src`. Mit Keycloak als Anmeldung zeigt die Vorschau die Seite des Coworkee-Auth-Servers, die es dort nicht gibt; dort übernimmt die Keycloak-Karte das Design.

Die Karte sehen Administratoren der Systemorganisation, die Einstellungen verwalten dürfen (`Settings.Manage`), denn der Abschnitt gilt für alle Organisationen. Für alle anderen und solange `AllowDesignChanges` aus ist, bleibt sie verborgen.

**Wie schnell der Auth-Server folgt.** Der Auth-Server liest das Design bei jeder Anfrage aus seiner Konfiguration. Eine Änderung sieht er, sobald seine Datenbank-Konfigurationsquelle neu lädt: `AddCoworkeeDatabaseConfiguration` prüft standardmäßig alle 15 Sekunden (der Auth-Host der Vorlage fügt sie hinzu). Ein Host im selben Prozess wie die API sieht die Änderung sofort. Farben, Schrift, Logo und der Designschlüssel der Themes, die die Seiten zeigen dürfen, werden eine Minute zwischengespeichert.

Die Client-Optionen steuern die Karte:

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.AllowSignInDesign = false;        // blendet die Karte aus
    options.SignInLayouts.Add("Ribbon");      // bietet ein eigenes Layout an
});
```

Ein eigenes Layout erhält eine allgemeine Vorschau mit seinem Namen. Themes, die noch den früheren Wert je Theme tragen (`SignIn` in den Theme-Optionen), werden ignoriert; gib ihnen stattdessen ein benanntes Design.

## Eigene Designs

Die Layouts sind Razor-Layouts neben den Seiten der Bibliothek: `Pages/_DesignCard.cshtml`, `Pages/_DesignSplit.cshtml`, `Pages/_DesignSlide.cshtml`, `Pages/_DesignSpotlight.cshtml`, `Pages/_DesignPoster.cshtml`, `Pages/_DesignStack.cshtml` und `Pages/_DesignMinimal.cshtml`, alle innerhalb von `Pages/_Layout.cshtml`, das Kopf, Theme-Variablen, Stylesheet und Skript rendert. Wie bei jeder Razor-Klassenbibliothek gewinnt eine View des Auth-Hosts unter demselben Pfad:

- **Eigenes Layout**: `Pages/_DesignRibbon.cshtml` im Auth-Host anlegen und `Design:Layout` auf `Ribbon` setzen (oder auf der Seite *Anmeldung* wählen).
- **Ein mitgeliefertes ersetzen**: `Pages/_DesignCard.cshtml` im Auth-Host anlegen.
- **Alles ersetzen**: `Pages/_Layout.cshtml` anlegen; dann gehören auch Stylesheet und Skript dir.

```cshtml
@* Pages/_DesignRibbon.cshtml im Auth-Host *@
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@inject Coworkee.AuthServer.AuthBrandingProvider Branding
@{
  Layout = "_Layout";
  ViewData["Design"] = "Ribbon";
  var brand = await Branding.GetAsync(Context.RequestAborted);
}
<div class="ribbon">
  <main class="panel">
    <partial name="_Brand" model="brand" />
    @RenderBody()
  </main>
</div>
<style>
  body.layout-ribbon { display: grid; place-items: center; background: radial-gradient(circle at top, var(--signal), var(--paper) 60%); }
  .ribbon .panel { padding: 40px; background: var(--surface); border-radius: calc(var(--radius) * 3); }
</style>
```

`AuthBranding` enthält App-Namen, Logo, das CSS des Themes und das aufgelöste `Design`; die Partial-View `_Media` rendert dessen Bild oder Video. Das Seitenmodell ist `ViewContext.ViewData.Model` (`LoginModel`, `RegisterModel`, ...), so kann ein Layout die Seiten unterscheiden. Das Stylesheet definiert diese Variablen, die das Theme füllt: `--paper`, `--surface`, `--ink`, `--ink-2`, `--rule`, `--signal`, `--on-signal`, `--accent`, `--tertiary`, `--error`, `--success`, `--radius` und `--font`; `_Layout.cshtml` ergänzt die Füllungen als `--page-fill` und `--panel-fill` an `<body>` (mit den Klassen `has-background`, `page-dark`/`page-light`, `panel-dark`/`panel-light`), `AuthBranding.Options` sagt, was das Layout zeigt. Die Content Security Policy erlaubt Inline-Styles, Skripte aber nur vom Auth-Server selbst.
