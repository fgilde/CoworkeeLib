# Coworkee Studio

Coworkee Studio ist der Desktop-Assistent der [Kommandozeile](cli.md). Er führt Seite für Seite durch eine neue App, erklärt, was jede Entscheidung bringt und kostet, zeigt Logo, Theme und Anmeldedesign als Vorschau, nimmt Ihre Entities auf und lässt die CLI die Solution anlegen. Er läuft unter Windows, macOS und Linux.

![Coworkee Studio](../assets/screenshots/studio-architecture-light-de.webp)

## Installieren und starten

```bash
dotnet tool install -g Coworkee.Cli
coworkee ui
```

Mehr braucht es nicht: `coworkee ui` installiert das Studio (globales Tool `Coworkee.Studio`, Befehl `coworkee-ui`) in der Version der CLI, wenn es fehlt, aktualisiert es, wenn es älter ist, und startet es. Weitere Argumente gehen an das Studio:

| Argument | |
|---|---|
| `--spec <datei>` | Öffnet eine [App-Spezifikation](cli.md#app-spezifikationen) |
| `--output <ordner>` | Ordner, in dem der Ordner der App angelegt wird |
| `--theme light` oder `dark` | Farbmodus (Standard: der des Systems) |
| `--lang en` oder `de` | Sprache (Standard: die des Systems) |

```bash
coworkee ui --spec shop.json
```

Für lokale Builds von Coworkee: `coworkee ui --add-source <ordner> --studio-version <version>`.

## Die Seiten

Das Panel links zeigt, wo Sie sind; jede Seite, die Sie schon gesehen haben, ist einen Klick entfernt. `Alt+→` und `Alt+←` blättern vor und zurück, `Strg+O` und `Strg+S` öffnen und speichern eine Spezifikation, `F1` öffnet die Hilfe. Das Abzeichen oben zeigt, ob die Spezifikation gültig ist; Probleme erscheinen auf der Seite, zu der sie gehören.

### Willkommen

Eine neue App beginnen, eine Spezifikation öffnen (`.json` aus der CLI, dem [MCP-Server](mcp.md) oder einer früheren Sitzung) oder eine der zuletzt geöffneten wählen.

![Willkommen](../assets/screenshots/studio-welcome-light-de.webp)

### Grundlagen

Der Name wird Präfix jedes Projekts und Namespaces (`Shop.Api`, `Shop.Web`) und wird deshalb wie in der CLI geprüft: ein Buchstabe, dann Buchstaben oder Ziffern. Den Titel lesen die Benutzer im Browser, in der App-Leiste, auf den Anmeldeseiten und im Aspire-Dashboard. Der Speicherort ist der Ordner, in dem der Ordner der App angelegt wird.

![Grundlagen](../assets/screenshots/studio-basics-light-de.webp)

### Branding

Logo, die sieben [eingebauten Themes](../modules/theming.md) als Karten in hell und dunkel und Markenfarben mit Farbwähler. Die Live-Vorschau zeichnet die Oberfläche der App (App-Leiste, Navigation, Karten, Schaltflächen) in Farben, Schrift und Eckenradius des Themes, hell und dunkel nebeneinander. Mit Markenfarben bekommt die App ein eigenes Theme, benannt wie der Titel und basierend auf dem gewählten.

![Branding](../assets/screenshots/studio-branding-light-de.webp)

### Anmeldung

Karte, Split oder Slide, gezeichnet als kleine animierte Skizzen der echten [Anmeldeseiten](../security/sign-in-pages.md), mit Bild oder Video neben dem Formular und dessen Seite.

![Anmeldung](../assets/screenshots/studio-signin-light-de.webp)

### Architektur

Vorlagen für den App-Typ (Minimal, Business-App, Teamarbeit, SaaS-Plattform, Alles) setzen Module und Infrastruktur; danach lässt sich jede Option von `coworkee options` ändern: Beschreibung, Vor- und Nachteile, wann sie passt, das Paket und der Schalter von `coworkee new`. Das Panel rechts zeigt, was die Solution enthalten wird: Projekte, die Container des Aspire-AppHosts (Postgres, Redis, Mailpit, Elasticsearch, RabbitMQ, Azurite, Keycloak) und die Coworkee-Pakete.

![Architektur](../assets/screenshots/studio-architecture-light-de.webp)

### Entities

Feature-Module mit ihren Entities: Name, Plural, Client-Entity, DTO-Mapping und ein Raster der Eigenschaften (Name, Typ, Pflicht, maximale Länge). Probleme erscheinen am Modul oder an der Entity, die sie betreffen. Das Panel rechts zeigt, was die CLI für die gewählte Entity erzeugt: Route der Seite, Route der API, Namen der Berechtigungen und Dateien. **Beispiel** fügt ein Sales-Modul mit Kunden und Aufträgen hinzu.

![Entities](../assets/screenshots/studio-entities-light-de.webp)

### Prüfen

Die Zusammenfassung, die Prüfung nach den Regeln der CLI, die Spezifikation als JSON (kopieren oder speichern) und die passenden Befehlszeilen: `coworkee new --spec` mit allem und `coworkee new` mit Schaltern für dieselbe App ohne Entities. Logo und Medien aus anderen Ordnern werden beim Speichern neben die Spezifikation kopiert, denn die CLI liest Dateien einer Spezifikation nur aus deren Ordner.

![Prüfen](../assets/screenshots/studio-review-light-de.webp)

### Anlegen

Das Studio schreibt die Spezifikation, ruft `coworkee spec validate` und `coworkee new --spec` auf und zeigt jeden Schritt der CLI, sobald er beginnt, auf Wunsch mit der ganzen Ausgabe. Ausgabeordner, Logo und Medien gehen auf die Befehlszeile. Danach: Ordner öffnen, in VS Code, Rider oder Visual Studio öffnen oder die App mit `coworkee run` in einem neuen Terminal starten.

![Anlegen](../assets/screenshots/studio-generate-light-de.webp)

Ist die CLI nicht installiert, bietet das Studio an, `dotnet tool install -g Coworkee.Cli` auszuführen oder eine lokale CLI zu verwenden (`coworkee.exe` oder `Coworkee.Cli.dll`).

## Hilfe und der MCP-Server

Die Hilfe (`F1` oder das Fragezeichen) erklärt den Assistenten, die Befehle der CLI und den [MCP-Server](mcp.md): was `coworkee mcp` ist, seine Werkzeuge und die Registrierung für Claude Code, Claude Desktop und VS Code mit Kopierknöpfen.

![Hilfe mit dem MCP-Server](../assets/screenshots/studio-help-light-de.webp)

## Einstellungen

Die Einstellungen in der Hilfe: Sprache (System, Englisch, Deutsch), Farbmodus, die zu verwendende CLI (leer für das globale Tool, eine Programmdatei oder `Coworkee.Cli.dll`), eine Quelle der Vorlage (`.nupkg` oder Ordner, als `--template-source` übergeben) und ein lokaler Paket-Feed, den das Restore der neuen App neben nuget.org nutzt. Sie liegen pro Benutzer zusammen mit den zuletzt geöffneten Spezifikationen im Anwendungsdatenordner unter `Coworkee/Studio/settings.json`.
