# Kommandozeile

Das Tool `coworkee` legt eine Coworkee-Anwendung aus dem Paket `Coworkee.Templates` an, mit den Modulen, dem Branding und den Entities Ihrer Wahl, und übernimmt die alltäglichen Befehle: starten, Migrationen, neue Module und Entities, Paket-Updates. Außerdem startet es einen [MCP-Server](mcp.md) für KI-Agenten und öffnet Coworkee Studio, einen Desktop-Assistenten.

## Installation

```bash
dotnet tool install -g Coworkee.Cli
coworkee doctor
```

`coworkee doctor` prüft das .NET SDK (mindestens die Version aus `global.json`), ob Docker läuft und ob `dotnet-ef` installiert ist. Die Aspire-CLI ist optional; `coworkee run` nutzt `dotnet run`.

## Eine neue Anwendung

```bash
coworkee new Shop
cd Shop
coworkee run
```

`coworkee new` installiert `Coworkee.Templates` in der Version des Tools, legt die Solution an, erzeugt die Module einer Spezifikation, stellt die Pakete wieder her und legt die Migration an, die die gewählten Module brauchen. Die erzeugte App verweist auf die Coworkee-Pakete genau dieser Version. Der Name muss einfach sein: ein Buchstabe, dann Buchstaben oder Ziffern (`Shop`, `Crm2`). Er wird zum Präfix aller Projekte und Namespaces.

Der Migrations-Worker legt einen Administrator `admin@<name>.local` an. Sein Passwort wurde beim Anlegen der Solution erzeugt und steht in `src/<Name>.Migrations/DemoSeed.cs`; ändern Sie es, bevor Sie irgendwohin deployen.

## Optionen

Jede Option ist ein Schalter von `coworkee new` und, unter ihrer Id, ein Schlüssel in den `options` einer [Spezifikation](#app-spezifikationen). `coworkee options` zeigt sie mit den Gründen, sie zu wählen, `coworkee options --json` als Daten.

```bash
coworkee new Shop --no-samples --chat false --files --event-bus rabbitMq \
  --theme Forest --primary-color "#0a7d5a" --logo ./brand/logo.svg \
  --sign-in-layout Split --sign-in-media ./brand/welcome.mp4 --git
```

| Schalter | Id | Werte | Standard | |
|---|---|---|---|---|
| `-o`, `--output` | | Ordner | `./<name>` | Zielordner |
| `--spec` | | Datei | | Legt die App aus einer Spezifikation an, siehe unten; Schalter gewinnen gegen die Spezifikation |
| `--template-source` | | Datei oder Ordner | nuget.org | Template aus einer `.nupkg` oder einem Ordner installieren, offline oder für einen lokalen Build |
| `--title` | `title` | Text | der Name | Anzeigetitel im Browser, in der App-Leiste, auf den Anmeldeseiten und im Aspire-Dashboard |
| `--samples`, `--no-samples` | `samples` | bool | `true` | Module Catalog (Produkte, Marken, Dashboard) und Documents mit Seiten, Tests und Demodaten |
| `--tests`, `--no-tests` | `tests` | bool | `true` | API-, Migrations-, App-Host- und Seitentests |
| `--git` | `git` | bool | `false` | `git init` im neuen Ordner |
| `--auditing` | `auditing` | bool | `true` | Seiten für Audit-Log, Verlauf und Wiederherstellen; das Protokoll selbst wird immer geschrieben |
| `--notifications` | `notifications` | bool | `true` | Benachrichtigungen in der App mit Sammel-Mails |
| `--chat` | `chat` | bool | `true` | Direkt- und Gruppenchat |
| `--ai` | `ai` | bool | `true` | KI-Assistent, MCP-Endpunkt der App und Protokoll der Tool-Aufrufe |
| `--backup` | `backup` | bool | `true` | Datenbank-Backups aus den Admin-Seiten |
| `--files` | `files` | bool | `false` | Ordner und Dateien mit Vorschau und Rechten pro Ordner |
| `--features` | `features` | bool | `false` | Features, Editionen und die Mandantenverwaltung |
| `--social` | `social` | bool | `false` | Kommentare, Tags und Bewertungen |
| `--extended-attributes` | `extendedAttributes` | bool | `false` | Schlüssel/Wert-Attribute für beliebige Entities (das Documents-Beispiel bringt sie ohnehin mit) |
| `--elasticsearch` | `elasticsearch` | bool | `false` | Elasticsearch-Index; der App-Host ergänzt den Container |
| `--event-bus` | `eventBus` | `none`, `inProcess`, `rabbitMq` | `none` | Integrationsereignisse über die Outbox; `rabbitMq` ergänzt den Transport und einen RabbitMQ-Container |
| `--client-entities` | `clientEntities` | bool | `false` | Browser-Kopien von OData-Sets mit `[ClientEntity]`; Entities der Spezifikation mit `clientEntity` schalten es ein |
| `--keycloak` | `keycloak` | bool | `false` | Keycloak-Container als externe Anmeldung |
| `--storage` | `storage` | `fileSystem`, `azureBlob` | `fileSystem` | Blob-Speicher; `azureBlob` startet Azurite im App-Host |
| `--theme` | `theme` | `Ocean`, `Coworkee`, `Classic`, `Forest`, `Midnight`, `Sunset`, `HighContrast` | `Ocean` | Standard-Theme |
| `--primary-color` | `primaryColor` | `#rrggbb` | | Markenfarbe: Die App bekommt ein eigenes Theme auf Basis von `--theme`, benannt wie der Titel, als Standard |
| `--secondary-color` | `secondaryColor` | `#rrggbb` | | Zweite Markenfarbe |
| `--logo` | `logo` | Datei | Coworkee-Symbol | Wird in den Web-Client kopiert; App-Leiste, Browser-Tab und Anmeldeseiten |
| `--sign-in-layout` | `signInLayout` | `Card`, `Split`, `Slide` | `Slide` | Layout der [Anmeldeseiten](../security/sign-in-pages.md) |
| `--sign-in-media` | `signInMedia` | Datei oder http(s)-Adresse | | Bild oder Video neben dem Anmeldeformular; eine Datei wird nach `wwwroot/media` des Web-Clients kopiert |
| `--sign-in-media-side` | `signInMediaSide` | `Left`, `Right` | `Left` | Seite des Mediums |
| `--mapping` | `mapping` | `manual`, `mapperly` | `manual` | DTO-Mapping erzeugter Entities, siehe [DTOs und Mapping](../fundamentals/dtos.md) |

Ein bool-Schalter ohne Wert bedeutet `true` (`--files`), `--chat false` schaltet aus. Die mitgelieferte erste Migration deckt die Standardmodule ab (mit oder ohne Beispiele). Schalten Sie ein Modul mit Tabellen ein oder aus (Chat, KI, Backup, Benachrichtigungen, Dateien, Features, Social, Event-Bus, erweiterte Attribute ohne Beispiele), lässt das Template die Migration weg und `coworkee new` legt nach dem Restore `Initial` mit `dotnet ef` an. Mit reinem `dotnet new` führen Sie `coworkee migrations add Initial` einmal selbst aus.

## App-Spezifikationen

Eine Spezifikation beschreibt die ganze App als JSON: Name, Optionen, Branding und Feature-Module mit ihren Entities. Coworkee Studio und der [MCP-Server](mcp.md) arbeiten mit diesem Format.

```bash
coworkee spec example > shop.json    # ein vollständiges Beispiel
coworkee spec schema > spec.schema.json
coworkee spec validate shop.json
coworkee new --spec shop.json
```

```json title="shop.json"
{
  "name": "Shop",
  "output": "shop",
  "options": { "samples": false, "chat": false, "files": true, "mapping": "manual" },
  "branding": {
    "title": "Acme Shop",
    "theme": "Forest",
    "primaryColor": "#0a7d5a",
    "logo": "brand/logo.svg",
    "signInLayout": "Split",
    "signInMedia": "brand/welcome.mp4"
  },
  "modules": [
    {
      "name": "Sales",
      "entities": [
        {
          "name": "Customer",
          "properties": [
            { "name": "Name", "required": true, "maxLength": 200 },
            { "name": "Email", "maxLength": 200 },
            { "name": "IsActive", "type": "bool" }
          ]
        },
        {
          "name": "Order",
          "clientEntity": true,
          "mapping": "mapperly",
          "properties": [
            { "name": "Number", "required": true, "maxLength": 50 },
            { "name": "Total", "type": "decimal", "required": true },
            { "name": "OrderedAt", "type": "dateTime" }
          ]
        }
      ]
    }
  ]
}
```

- `options` nimmt die Ids aus der Tabelle oben; `branding` nimmt `title`, `theme`, `primaryColor`, `secondaryColor`, `logo`, `signInLayout`, `signInMedia` und `signInMediaSide` und gewinnt gegen dieselben Ids in `options`.
- Dateien und `output` sind relativ zur Spezifikation und müssen in ihrem Ordner bleiben (keine absoluten Pfade, kein `..`). Ein Logo ist ein Bild (svg, png, jpg, jpeg, webp, gif, ico), das Anmeldemedium ein Bild, ein Video (mp4, webm, ogg) oder eine http(s)-Adresse: Beide liegen danach öffentlich im Web-Client. Eine als Schalter angegebene Datei (`--logo C:\brand\logo.svg`) darf überall liegen. Kommentare und abschließende Kommas sind erlaubt.
- Der Titel landet unverändert in Code und HTML und darf daher kein `"`, `\`, `<`, `>`, `@` und keine Steuerzeichen enthalten.
- Typen der Eigenschaften: `string`, `int`, `long`, `decimal`, `double`, `bool`, `dateTime` (`DateTimeOffset`), `guid`. Eine Eigenschaft ohne `required` ist nullable; `maxLength` gilt für Strings.
- `plural` benennt Tabelle, OData-Set, Route und Seite (Standard: ein englischer Plural des Namens). `mapping` übersteuert die Option `mapping`.

`coworkee spec validate` und `coworkee new` melden jedes Problem mit seinem Pfad, zum Beispiel `modules[0].entities[1].properties[0].name: 'Id' is taken by the entity itself.`

## Module und Entities

Jedes Modul einer Spezifikation, und `coworkee module add`, legt das Projekt `src/<App>.<Modul>` so an, wie [Das erste Feature](first-feature.md) die Marken baut. Pro Entity:

| Wo | Was |
|---|---|
| Contracts | `<Entity>Dto`, `AddEdit<Entity>Request`, `<Modul>Permissions.<Plural>` (ansehen, anlegen, bearbeiten, löschen) |
| Modul | Entity (`AuditedEntity`, `IMultiTenant`, `[Realtime]`, auf Wunsch `[ClientEntity]`), Tabelle mit Maximallängen, Rechtedefinitionen, OData-Set, Anlegen/Bearbeiten-Command mit Rechteprüfung und Validator, Lösch-Command, Mapping (`ToDto`-Methode oder Mapperly-Mapper), Endpunkte `POST`, `PUT /{id}` und `POST /delete` unter `/api/v1/<plural>` |
| Web-Client | Seite `/<modul>/<plural>` mit Datentabelle, Anlegen, Bearbeiten und Löschen, Live-Aktualisierung, API-Client und einem Navigationseintrag in der Gruppe des Moduls |

Das Modul kommt in die `.slnx`, das Infrastrukturprojekt verweist darauf und `<App>DatabaseModule` hängt davon ab; `Program.cs` des Web-Clients registriert die API-Clients und Navigationseinträge.

```bash
coworkee module add Sales --entity Order --property Number:string:required:50 --property Total:decimal --client-entity
coworkee entity add Sales Invoice -p Number:required:30 -p DueAt:dateTime -p Paid:bool --mapping Mapperly
coworkee migrations add AddSales
```

`--property` liest `Name[:typ][:required][:maxLänge]` (Standard: `Name:string:required:200`). `entity add` braucht ein von der CLI angelegtes Modul.

## Coworkee Studio

```bash
coworkee ui
coworkee ui --spec shop.json
```

`coworkee ui` startet Coworkee Studio, einen Desktop-Assistenten für Name, Titel, Logo, Farben, Module, Anmeldedesign, Theme und Entities; er legt die App mit dieser CLI an. Das Studio ist das globale Tool `Coworkee.Studio` (Befehl `coworkee-ui`): `coworkee ui` installiert es in der Version der CLI, wenn es fehlt, und aktualisiert es, wenn es älter ist. Weitere Argumente gehen an das Studio. Für lokale Builds: `--prerelease`, `--add-source <ordner>` und `--studio-version <version>`; eine ausdrücklich genannte Version, die älter als die installierte ist, wird ebenfalls installiert.

## Weitere Befehle

| Befehl | Was er tut |
|---|---|
| `coworkee run` | Startet das `*.AppHost`-Projekt der Solution |
| `coworkee migrations add <name>` | `dotnet ef migrations add <name>` mit dem `*.Infrastructure`-Projekt als Projekt und Startprojekt |
| `coworkee module add <name>` | Ein Feature-Modul mit seiner ersten Entity, siehe oben |
| `coworkee entity add <modul> <name>` | Eine weitere Entity in einem Modul, siehe oben |
| `coworkee options [--json]` | Die Optionen von `coworkee new` |
| `coworkee spec schema`, `example`, `validate <datei>` | App-Spezifikationen |
| `coworkee update [--version <version>] [--prerelease]` | Setzt `<CoworkeeVersion>` in `Directory.Packages.props`, standardmäßig auf die neueste Version auf nuget.org |
| `coworkee doctor` | Prüft die Werkzeuge, siehe oben |
| `coworkee ui` | Coworkee Studio |
| `coworkee mcp` | [MCP-Server](mcp.md) über stdio |

Nach `coworkee update` führen Sie `dotnet restore` aus und legen eine Migration an, falls ein Coworkee-Modul sein Modell geändert hat (`coworkee migrations add CoworkeeUpdate`).

## Ausgabe für Werkzeuge

`coworkee new` schreibt vor jedem Schritt eine Zeile `==> <schritt>` (Template installieren, Solution anlegen, Modul erzeugen, Restore, Migration anlegen, Git), vor jedem Befehl, den es ausführt, `> <befehl>`, und endet mit einem Code ungleich 0, wenn ein Schritt scheitert. Probleme mit der Eingabe stehen einzeln pro Zeile auf stderr.

## Ohne das Tool

Das Template funktioniert auch mit reinem `dotnet new`; die Schalter heißen wie die Ids der Tabelle (`--eventBus`, `--primaryColor`, ...):

```bash
dotnet new install Coworkee.Templates
dotnet new coworkee -n Shop --samples false --keycloak true --title "My Shop" --theme Sunset
dotnet run --project Shop/src/Shop.AppHost
```

`dotnet new coworkee --help` listet sie auf. Kopieren Sie ein Logo selbst nach `src/<Name>.Web.Client/wwwroot`, bevor Sie `--logo <dateiname>` angeben.
