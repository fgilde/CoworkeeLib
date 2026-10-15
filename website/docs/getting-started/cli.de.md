# Kommandozeile

Das Tool `coworkee` legt eine Coworkee-Anwendung aus dem Paket `Coworkee.Templates` an, mit den Modulen, dem Branding und den Entities deiner Wahl, und übernimmt die alltäglichen Befehle: starten, Migrationen, neue Module und Entities, Paket-Updates. Außerdem startet es einen [MCP-Server](mcp.md) für KI-Agenten und öffnet Coworkee Studio, einen Desktop-Assistenten.

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

Der Migrations-Worker legt einen Administrator `admin@<name>.local` und die Benutzer von `--user` oder der Spezifikation an. Am Ende zeigt `coworkee new` einen Kasten **Sign in** mit jedem Benutzer, seinem Passwort und seinen Rollen; dieselbe Tabelle steht in `LOGIN.md` der Solution. Mit Keycloak hat der Realm dieselben Benutzer mit denselben Passwörtern.

```bash
coworkee new Shop --keycloak --user "ada@shop.test:Product Manager" --user boss@shop.test:admin
```

`--user` nimmt `email[:rolle,rolle][:admin]` und lässt sich wiederholen; das Passwort wird erzeugt. Die Benutzer stehen in `src/<Name>.Migrations/DemoUsers.cs`, das der Seed und der AppHost (für Keycloak) beide lesen. Der Seed läuft nur in der Umgebung Development; ein Deployment startet ohne Benutzer und wird im Browser eingerichtet (siehe [Deployment](../hosting/deployment.md)).

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
| `--airworks` | `airworks` | bool | `false` | [AirWorks-Client](../web/airworks.md): ein Web-Desktop neben der Blazor-App mit derselben Anmeldung und API, Apps für fast alles, was die Blazor-App bietet (Einstellungen, Dateien, Daten, Chat, Berichte mit Designer, Assistent, Verwaltung); braucht Node.js |
| `--airworks-packages` | `airworksPackages` | `bundled`, `npm` | `bundled` | Woher der AirWorks-Client `@coworkee/client` und `@coworkee/airworks` nimmt: aus den Tarballs im Template oder von npm in der Version des Templates |
| `--pwa` | `pwa` | bool | `true` | [Installierbare App](../ui/phones-and-pwa.md) mit Service Worker und Offline-Seite; mit `--notifications` auch Web-Push pro Gerät (`options.UsePush()` im App-Host) |
| `--repository` | `repository` | Text | | GitHub-Repository als `owner/repo`: Die README bekommt ein [QuickRun](https://quickrun.org)-Badge. Jede App hat eine `quickrun.yml`, die den App-Host startet |
| `--auditing` | `auditing` | bool | `true` | Seiten für Audit-Log, Verlauf und Wiederherstellen; das Protokoll selbst wird immer geschrieben |
| `--notifications` | `notifications` | bool | `true` | Benachrichtigungen in der App mit Sammel-Mails |
| `--chat` | `chat` | bool | `true` | Direkt- und Gruppenchat |
| `--ai` | `ai` | bool | `true` | KI-Assistent, MCP-Endpunkt der App und Protokoll der Tool-Aufrufe |
| `--backup` | `backup` | bool | `true` | Datenbank-Backups aus den Admin-Seiten |
| `--files` | `files` | bool | `false` | Ordner und Dateien mit Vorschau und Rechten pro Ordner |
| `--features` | `features` | bool | `false` | Features, Editionen und die Mandantenverwaltung |
| `--social` | `social` | bool | `false` | Kommentare, Tags und Bewertungen |
| `--workflows` | `workflows` | bool | `false` | Zustände, Übergänge und Genehmigungen für Entities; mit den Beispielen durchlaufen Produkte eine Freigabe |
| `--elsa` | `elsa` | bool | `false` | Elsa Workflows 3 in der API (braucht `--workflows`) |
| `--webhooks` | `webhooks` | bool | `false` | Ausgehende Webhooks mit signierter Zustellung, Wiederholungen und Zustellprotokoll; mit den Beispielen senden Produkte Ereignisse |
| `--extended-attributes` | `extendedAttributes` | bool | `false` | Schlüssel/Wert-Attribute für beliebige Entities (das Documents-Beispiel bringt sie ohnehin mit) |
| `--elasticsearch` | `elasticsearch` | bool | `false` | Elasticsearch-Index; der App-Host ergänzt den Container |
| `--event-bus` | `eventBus` | `none`, `inProcess`, `rabbitMq` | `none` | Integrationsereignisse über die Outbox; `rabbitMq` ergänzt den Transport und einen RabbitMQ-Container |
| `--client-entities` | `clientEntities` | bool | `false` | Browser-Kopien von OData-Sets mit `[ClientEntity]`; Entities der Spezifikation mit `clientEntity` schalten es ein |
| `--keycloak` | `keycloak` | bool | `false` | Keycloak-Container als externe Anmeldung |
| `--storage` | `storage` | `fileSystem`, `azureBlob` | `fileSystem` | Blob-Speicher; `azureBlob` startet Azurite im App-Host |
| `--ai-provider` | `aiProvider` | `anthropic`, `openAI`, `azureOpenAI`, `gemini`, `openRouter`, `mistral`, `groq`, `deepSeek`, `xai`, `together`, `fireworks`, `ollama`, `lmStudio`, `openAICompatible` | `anthropic` | Modellanbieter des [Assistenten](../modules/ai.md#anbieter); `ollama` startet Ollama im App-Host, `lmStudio` nutzt LM Studio auf demselben Rechner |
| `--ai-model` | `aiModel` | Text | | Modell des Anbieters (Azure OpenAI: der Deployment-Name); leer nimmt den Standard des Anbieters |
| `--ai-endpoint` | `aiEndpoint` | http(s)-Adresse | | Basisadresse für Azure OpenAI, `openAICompatible` oder einen Anbieter hinter einem Proxy; der Schlüssel kommt in die User Secrets des App-Hosts (`Parameters:ai-apikey`), nie in die Spezifikation |
| `--redis` | `redis` | bool | `true` | Redis-Container für Backplane, Anwesenheit, Cache-Invalidierung und Sitzungen über Instanzen hinweg ([Redis](../hosting/redis.md)) |
| `--grafana` | `grafana` | bool | `true` | Grafana mit Prometheus, Loki, Tempo und Collector: Telemetrie jedes Dienstes, die App-Datenbank und Dashboards ([Grafana und WebDataStudio](../hosting/observability.md)) |
| `--web-data-studio` | `webDataStudio` | bool | `true` | WebDataStudio, verbunden mit der App-Datenbank ([Grafana und WebDataStudio](../hosting/observability.md)) |
| `--aspire-ui` | `aspireUI` | bool | `false` | AspireUI im AppHost, um weitere Apps und Stacks neben der App zu hosten und auszuprobieren; mit Keycloak meldet es sich über den Realm der App an ([AspireUI](../hosting/aspire.md#aspireui-in-einer-generierten-app)) |
| `--database` | `database` | `postgres`, `sqlServer`, `sqlite` | `postgres` | Datenbank-Engine, siehe [Datenbank-Provider](../fundamentals/persistence.md#datenbank-provider); `sqlServer` startet einen SQL-Server-Container, `sqlite` eine Datei ohne Container. Sicherungen brauchen `postgres` |
| `--theme` | `theme` | `Ocean`, `Coworkee`, `Classic`, `Forest`, `Midnight`, `Sunset`, `HighContrast` | `Ocean` | Standard-Theme |
| `--primary-color` | `primaryColor` | `#rrggbb` | | Markenfarbe: Die App bekommt ein eigenes Theme auf Basis von `--theme`, benannt wie der Titel, als Standard |
| `--secondary-color` | `secondaryColor` | `#rrggbb` | | Zweite Markenfarbe |
| `--logo` | `logo` | Datei | Coworkee-Symbol | Wird in den Web-Client kopiert; App-Leiste, Browser-Tab und Anmeldeseiten |
| `--sign-in-layout` | `signInLayout` | `Card`, `Split`, `Slide` | `Slide` | Layout der [Anmeldeseiten](../security/sign-in-pages.md) |
| `--sign-in-media` | `signInMedia` | Datei oder http(s)-Adresse | | Bild oder Video neben dem Anmeldeformular (Split, Slide) oder hinter dem Ladebildschirm (`--loading-screen SignInMedia`, jedes Layout); eine Datei wird nach `wwwroot/media` des Web-Clients kopiert |
| `--sign-in-media-side` | `signInMediaSide` | `Left`, `Right` | `Left` | Seite des Mediums |
| `--loading-screen` | `loadingScreen` | `Animated`, `SignInMedia`, `Plain` | `Animated` | Hintergrund des [Ladebildschirms](../modules/theming.md#ladebildschirm) |
| `--mapping` | `mapping` | `manual`, `mapperly` | `manual` | DTO-Mapping erzeugter Entities, siehe [DTOs und Mapping](../fundamentals/dtos.md) |
| `--languages` | `languages` | `en`, `de`, `fr`, `es`, `it`, `pt`, `pt-BR`, `nl`, `pl`, `ja`, `zh` und 28 weitere (`coworkee options`), durch Kommas getrennt (in einer Spezifikation ein Array) | `en`, `de` | Sprachen von Anfang an; die ohne mitgelieferte Texte werden beim ersten Start [maschinell übersetzt](../modules/localization.md#maschinelle-ubersetzung) |
| `--default-language` | `defaultLanguage` | eine der Sprachen | `en` | Sprache der Benutzer, die keine gewählt haben; muss unter `languages` sein |
| `--translation-provider` | `translationProvider` | `libreTranslate`, `myMemory`, `deepL`, `azure`, `google`, `ai`, `none` | `libreTranslate` | [Übersetzungsdienst](../modules/localization.md#maschinelle-ubersetzung); `libreTranslate` startet einen Container mit den Modellen der Sprachen, die mit Schlüssel lesen `Parameters:translation-key` (und `translation-region`) aus den User Secrets des AppHosts. `none` geht nur mit den mitgelieferten Sprachen, `ai` braucht den KI-Assistenten |
| `--translation-source` | `translationSource` | `en`, `de` | `en` | Sprache, von der aus übersetzt wird |
| `--color-mode` | `colorMode` | `system`, `light`, `dark` | `system` | Standard-Farbmodus, angelegt als systemweite Einstellung `Theme.Mode` |
| `--registration` | `registration` | bool | `true` | Selbstregistrierung auf der Anmeldeseite, angelegt als Einstellung `Account.AllowRegistration` |
| `--github-actions` | `githubActions` | bool | `false` | `.github/workflows/ci.yml`: Build und Tests bei jedem Push und Pull Request |
| `--devcontainer` | `devcontainer` | bool | `false` | `.devcontainer/devcontainer.json` mit dem .NET SDK und Docker in Docker (Codespaces, VS Code) |

Ein bool-Schalter ohne Wert bedeutet `true` (`--files`), `--chat false` schaltet aus.

**Optionen, die eine andere brauchen.** Manche Optionen gelten nur mit einer anderen: `--airworks-packages` mit `--airworks`, `--ai-provider`, `--ai-model` und `--ai-endpoint` mit dem KI-Assistenten (`--ai`), `--backup` mit `--database postgres`, `--sign-in-media` mit dem Layout `Split` oder `Slide` und `--sign-in-media-side` mit einem Anmeldemedium. Ohne sie fragt die CLI nicht danach (es gibt keine Rückfragen, jeder Wert ist ein Schalter oder ein Schlüssel der Spezifikation), gibt sie nicht weiter, die App ist also die ohne den Schalter, und warnt: `warn  airworksPackages: ignored, it only matters with airworks (coworkee options).` `coworkee spec validate` gibt dieselben Warnungen nach `ok` aus. `coworkee options` nennt die Voraussetzung (`Needs: airworks`), `--json` als `dependsOn`, und das Studio blendet die Karte aus, bis die andere Option an ist. `--backup true` mit einer anderen Datenbank als `postgres` bleibt ein Fehler.

**Links.** `coworkee options` gibt die Webseite dessen aus, was eine Option mitbringt (`More:`): AirWorks, AspireUI, WebDataStudio, Grafana, Keycloak, Redis, Elasticsearch, GitHub Actions und den Dev-Container; bei Auswahlen eine Zeile je Produkt (`More (postgres):`): PostgreSQL, SQL Server, SQLite, RabbitMQ, Azure Blob Storage, Mapperly, Ollama, LM Studio, LibreTranslate, MyMemory, DeepL, Azure AI Translator und Google Cloud Translation. `--json` enthält sie als `infoUrl` der Option und der Auswahl. Das Studio zeigt sie als Öffnen-Knopf auf der Karte.

Die mitgelieferte erste Migration deckt die Standardmodule ab (mit oder ohne Beispiele). Schaltest du ein Modul mit Tabellen ein oder aus (Chat, KI, Backup, Benachrichtigungen, Dateien, Features, Social, Workflows, Webhooks, Event-Bus, erweiterte Attribute ohne Beispiele) oder wählst eine andere Datenbank als `postgres`, lässt das Template die Migration weg und `coworkee new` legt nach dem Restore `Initial` mit `dotnet ef` an. Mit reinem `dotnet new` führst du `coworkee migrations add Initial` einmal selbst aus.

## App-Spezifikationen

Eine Spezifikation beschreibt die ganze App als JSON: Name, Optionen, Branding und Feature-Module mit ihren Entities. Coworkee Studio und der [MCP-Server](mcp.md) arbeiten mit diesem Format.

```bash
coworkee spec example > shop.json    # ein vollständiges Beispiel
coworkee spec schema > spec.schema.json
coworkee spec validate shop.json
coworkee spec show shop.json         # was die Spezifikation erzeugt: Bibliotheksmodule an und aus, Optionen, eigene Module
coworkee new --spec shop.json
```

Mit der CLI kommen sechs Startvorlagen (CRM, Helpdesk, Asset-Portal, kleiner Shop, Events, Anträge mit Freigabe): `coworkee specs list` zählt sie auf, `coworkee new Sales --template-spec crm` legt eine an, `coworkee specs show crm > crm.json` gibt sie aus, damit du sie vorher anpassen kannst. Auf der Seite [Startvorlagen](gallery.md) gibt es sie zum Herunterladen, dazu einen kleinen Assistenten, der eine Spezifikation im Browser schreibt.

```json title="shop.json"
{
  "name": "Shop",
  "output": "shop",
  "options": { "samples": false, "chat": false, "files": true, "mapping": "manual", "languages": [ "en", "de" ], "defaultLanguage": "de" },
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
            { "name": "Name", "required": true, "maxLength": 200, "display": true },
            { "name": "Email", "type": "email", "unique": true },
            { "name": "IsActive", "type": "bool", "default": "true", "facet": true }
          ]
        },
        {
          "name": "Order",
          "clientEntity": true,
          "mapping": "mapperly",
          "properties": [
            { "name": "Number", "required": true, "maxLength": 50, "unique": true },
            { "name": "Customer", "type": "reference", "reference": "Customer", "required": true, "inverse": "Orders", "facet": true },
            { "name": "Status", "type": "enum", "enum": "OrderStatus", "required": true, "default": "Open", "facet": true },
            { "name": "Total", "type": "money", "required": true },
            { "name": "OrderedOn", "type": "dateOnly", "label": "Order date" },
            { "name": "Note", "type": "text" }
          ]
        }
      ]
    }
  ],
  "enums": [ { "name": "OrderStatus", "values": [ "Open", "Paid", "Shipped" ] } ]
}
```

- `modules` sind deine eigenen Module mit ihren Entitäten. Die Module der Bibliothek (Audit-Log, Chat, Dateien, ...) sind Optionen unter `options`, und eine selbst geschriebene Spezifikation nennt nur, was von den Standardwerten abweicht: ohne `"chat": false` hat die App den Chat, eine Spezifikation der Standardwerte enthält also kein Modul. Das Studio schreibt deshalb jedes Bibliotheksmodul mit `true` oder `false`; `coworkee spec show` gibt dasselbe für jede Spezifikation aus (`Library modules on:` / `off:`, dann jede andere Option mit ihrem Wert, `(unused)` wo sie ignoriert wird, und deine eigenen Module).
- `options` nimmt die Ids aus der Tabelle oben; `branding` nimmt `title`, `theme`, `primaryColor`, `secondaryColor`, `logo`, `signInLayout`, `signInMedia`, `signInMediaSide` und `loadingScreen` und gewinnt gegen dieselben Ids in `options`.
- Dateien und `output` sind relativ zur Spezifikation und müssen in ihrem Ordner bleiben (keine absoluten Pfade, kein `..`). Ein Logo ist ein Bild (svg, png, jpg, jpeg, webp, gif, ico), das Anmeldemedium ein Bild, ein Video (mp4, webm, ogg) oder eine http(s)-Adresse: Beide liegen danach öffentlich im Web-Client. Eine als Schalter angegebene Datei (`--logo C:\brand\logo.svg`) darf überall liegen. Kommentare und abschließende Kommas sind erlaubt.
- Der Titel landet unverändert in Code und HTML und darf daher kein `"`, `\`, `<`, `>`, `@` und keine Steuerzeichen enthalten.
- Typen der Eigenschaften: `string`, `int`, `long`, `decimal`, `double`, `bool`, `dateTime` (`DateTimeOffset`), `guid`, `dateOnly`, `timeOnly`, `text` (langer Text, mehrzeilig im Formular, keine Listenspalte), `email` und `url` (Strings mit Prüfung, 256 und 2000 Zeichen, sofern `maxLength` nichts anderes sagt), `money` (Decimal mit `precision` 18 und `scale` 2, sofern nicht angegeben), `enum` und `reference`. Eine Eigenschaft ohne `required` ist nullable; `maxLength` gilt für Strings, E-Mails und URLs, `precision` und `scale` für Decimal und Money.
- `enum` nennt einen Eintrag aus `enums` (Werte in ihrer Reihenfolge, der erste ist der Standard). Das Enum landet in den Contracts, reist in JSON und OData mit seinem Namen und erscheint mit seinen Namen in Liste, Formular und Facetten.
- `reference` verweist auf eine andere Entity über ihren Namen (`Customer`) oder mit Modul (`Crm.Customer`): die Eigenschaft ist die Navigation, `<Name>Id` der Fremdschlüssel. `referenceDisplay` ist die Texteigenschaft des Ziels, die Liste und Auswahl zeigen (Standard: seine Anzeige-Eigenschaft), `onDelete` ist `restrict` (Standard), `cascade` oder `setNull` (nur für optionale Referenzen), `inverse` gibt dem Ziel eine Sammlung (nur im selben Modul). Ein Modul darf andere Module referenzieren, solange keines zurückverweist.
- `default` setzt den Anfangswert von Zahlen, Bools, Enums und Texten; `unique` legt einen eindeutigen Index je Mandant und eine Prüfung im Command an; `label` ist die Beschriftung von Spalte und Feld; `display` markiert den Text, der eine Zeile benennt; `searchable` (Standard: true für Texte) nimmt sie ins Suchfeld; `facet` bietet sie als Facette neben der Liste an (Bools, Enums, Referenzen, ganze Zahlen, Strings).
- `plural` benennt Tabelle, OData-Set, Route und Seite (Standard: ein englischer Plural des Namens). `mapping` übersteuert die Option `mapping`.

### Benutzer und Rollen

```json
"roles": [ { "name": "Sales", "description": "Takes orders", "permissions": [ "Sales.Customers.View", "Sales.Orders.View", "Sales.Orders.Create", "Sales.Orders.Edit" ] } ],
"users": [
  { "email": "ada@shop.test", "displayName": "Ada Lovelace", "roles": [ "Sales" ] },
  { "email": "grace@shop.test", "roles": [ "User" ], "admin": true, "password": "Grace-2026!" }
]
```

- `roles` werden neben den eigenen Rollen der Vorlage angelegt (mit Beispielen *Product Manager* und *Brand Manager*, ohne *User*). Die Berechtigungen müssen zur App gehören: die der gewählten Module (`Identity.Users.View`, `Chat.Use`, `Reports.View`, ...), der Beispiele und `<Modul>.<Plural>.View|Create|Edit|Delete` der Entities der Spezifikation; die Prüfung zählt sie auf.
- `users` brauchen eine E-Mail; `displayName` ist ohne Angabe der Teil vor `@`, `roles` nennt Rollen der Spezifikation oder der Vorlage, `admin` gibt alle Berechtigungen. Ohne `password` wird eines erzeugt; ein angegebenes braucht 8 Zeichen mit Groß- und Kleinbuchstaben, einer Ziffer und einem Sonderzeichen.

`coworkee spec validate` und `coworkee new` melden jedes Problem mit seinem Pfad, zum Beispiel `modules[0].entities[1].properties[0].name: 'Id' is taken by the entity itself.`

## Module und Entities

Jedes Modul einer Spezifikation, und `coworkee module add`, legt das Projekt `src/<App>.<Modul>` so an, wie [Das erste Feature](first-feature.md) die Marken baut. Pro Entity:

| Wo | Was |
|---|---|
| Contracts | `<Entity>Dto`, `AddEdit<Entity>Request`, `<Modul>Permissions.<Plural>` (ansehen, anlegen, bearbeiten, löschen) |
| Modul | Entity (`AuditedEntity`, `IMultiTenant`, `[Realtime]`, auf Wunsch `[ClientEntity]`, `[ProvideFacet]` an Facetten), Tabelle mit Maximallängen, Genauigkeiten, Fremdschlüsseln mit Löschverhalten und eindeutigen Indizes, Rechtedefinitionen, OData-Set, Anlegen/Bearbeiten-Command mit Rechteprüfung, Prüfung referenzierter Zeilen, Eindeutigkeitsprüfung und Validator (Pflicht, Längen, E-Mails, URLs, Enums), Lösch-Command, Mapping (`ToDto`-Methode oder Mapperly-Mapper; eine Referenz bringt Id und Anzeigetext ihres Ziels mit), Endpunkte `POST`, `PUT /{id}` und `POST /delete` unter `/api/v1/<plural>` |
| Contracts, einmal je Enum | `Enums/<Enum>.cs`, mit Namen serialisiert |
| Web-Client | Seite `/<modul>/<plural>` mit Datentabelle (Referenzen erweitert und mit ihrem Text angezeigt, Geldbeträge formatiert), Anlegen, Bearbeiten (Auswahlfelder für Referenzen, mehrzeilig für langen Text) und Löschen, Live-Aktualisierung, API-Client und einem Navigationseintrag in der Gruppe des Moduls |

Das Modul kommt in die `.slnx`, das Infrastrukturprojekt verweist darauf und `<App>DatabaseModule` hängt davon ab; `Program.cs` des Web-Clients registriert die API-Clients und Navigationseinträge.

```bash
coworkee module add Sales --entity Order --property Number:string:required:50:unique --property Total:money --client-entity
coworkee entity add Sales Invoice -p Number:required:30 -p Order:ref=Order:required:onDelete=cascade -p Status:enum=InvoiceStatus:default=Open:facet \
  --enum InvoiceStatus=Open,Paid --mapping Mapperly
coworkee entity add Sales Delivery -p Name:required:100 -p Carrier:ref=Logistics.Carrier
coworkee migrations add AddSales
```

`--property` liest `Name[:typ][:required][:maxLänge][:flag][:schlüssel=wert]` (Standard: `Name:string:required:200`). Flags: `required`, `unique`, `display`, `facet`, `searchable`. Schlüssel wie in einer Spezifikation: `enum`, `ref` (oder `reference`), `referenceDisplay`, `onDelete`, `inverse`, `default`, `label`, `precision`, `scale`, `searchable`. `--enum Name=Wert1,Wert2` legt ein Enum in den Contracts an. Eine Referenz darf auf jede Entity der App mit OData-Set zeigen, erzeugt oder aus den Beispielen (`ref=Catalog.Brand`); das Modul verweist dann auf das Projekt des Ziels. `entity add` braucht ein von der CLI angelegtes Modul.

## Entities importieren

Du hast schon eine Datenbank, eine API oder eine Tabelle? `coworkee entity import` macht daraus ein Modul:

```bash
coworkee entity import --from db "Host=localhost;Database=shop;Username=app;Password=..." --module Legacy --tables orders,customers
coworkee entity import --from db legacy.db --dry-run
coworkee entity import --from openapi https://localhost:7301/openapi/v1.json --module Store
coworkee entity import --from csv suppliers.csv --spec-out shop.json
coworkee migrations add ImportLegacy
```

| Quelle | Was wozu wird |
|---|---|
| `db` (Postgres, SQL Server, SQLite) | Tabellen werden Entities, Spalten Eigenschaften mit Typ, Pflicht (not null), maximaler Länge, Stellen und Nachkommastellen, eindeutige Indizes über eine Spalte `unique`, Fremdschlüssel über eine Spalte Referenzen (mit `cascade` oder `setNull`), Enum-Typen von Postgres Enums. Den Provider errät der Befehl aus der Verbindungszeichenfolge (eine vorhandene Datei ist SQLite); `--provider` legt ihn fest. |
| `openapi` (OpenAPI 3 oder Swagger 2, JSON) | Objekt-Schemas aus `components.schemas` werden Entities (ein Suffix `Dto` fällt weg, `allOf` wird zusammengeführt), String-Enums Enums, `$ref` Referenzen, `format` wählt Datum, Uhrzeit, UUID, E-Mail und URL; `customerId` neben einem importierten `Customer` wird ebenfalls eine Referenz. YAML-Dokumente wandelst du vorher in JSON um. |
| `csv` | die Kopfzeile benennt die Eigenschaften, die ersten 500 Zeilen verraten ihre Typen (bool, int, long, decimal, guid, Datum, Uhrzeit, Zeitpunkt, E-Mail, URL, String mit maximaler Länge) und ob sie Pflicht sind. |

Namen werden zu C#-Bezeichnern (aus `order_items` wird `OrderItems`), Tabellennamen zur Einzahl (aus `Orders` wird `Order`), Namen, die der erzeugte Code selbst nutzt, bekommen ein Suffix (aus `Task` wird `TaskItem`). Id-, Mandanten- und Audit-Spalten hat jede Entity von selbst, sie fallen weg; ein fachlicher Primärschlüssel bleibt als eindeutige Eigenschaft. Der Befehl zeigt jede Entity und was er ausgelassen hat: Verknüpfungstabellen einer Many-to-many-Beziehung (nicht unterstützt; eine eigene Entity mit zwei Referenzen erledigt das), JSON-, XML-, Binär- und Geo-Spalten, Fremdschlüssel über mehrere Spalten oder auf nicht importierte Tabellen, Listen in einer API.

Ohne `--spec-out` landet das Modul direkt in der App: ein neues Modul in einem Schritt, ein vorhandenes bekommt die Entities nacheinander, referenzierte zuerst. `--spec-out` schreibt das Modul stattdessen in eine Spezifikation (eine vorhandene behält alles andere), `--dry-run` gibt nur die Spezifikation und ihre Probleme aus. Geprüft wird alles wie eine Spezifikation, bevor etwas geschrieben wird.

## Eine bestehende App ändern

Optionen gibt es nicht nur bei `coworkee new`. `coworkee add` schaltet sie in einer App ein, an der du schon arbeitest, `coworkee remove` schaltet sie aus:

```bash
coworkee add reports --dry-run       # den Plan zeigen, nichts ändern
coworkee add reports
coworkee add eventBus rabbitMq       # oder eventBus=rabbitMq
coworkee add ai openAI               # schaltet ai ein und setzt aiProvider
coworkee add languages fr,it         # ergänzt die Sprachen der App
coworkee remove chat
coworkee add --spec shop.json        # Optionen, Module und Entities einer Spezifikation
```

Die CLI muss wissen, womit die App angelegt wurde. `coworkee new` hält das in `.coworkee/app.json` fest (jede Option, die vom Standard abweicht, deine Module und Entities, keine Passwörter); `coworkee add` schreibt die Datei fort. Bei Apps, die älter als diese Datei sind, liest die CLI die Optionen aus dem, was das Template geschrieben hat: Paketreferenzen, App-Host, Infrastrukturmodul und Seed. `coworkee spec current` zeigt, was sie sieht.

Dann legt sie das Template in der Coworkee-Version der App zweimal in einem temporären Ordner an, einmal mit den bisherigen und einmal mit den neuen Optionen, und führt den Unterschied in deine Dateien zusammen:

| Fall | Was passiert |
|---|---|
| Eine neue Datei des Templates | Sie kommt dazu |
| Eine Datei, die du nie angefasst hast | Sie wird zur neuen Fassung des Templates |
| Eine Datei, die du geändert hast, und die Änderung des Templates passt dazu | Beides wird zusammengeführt (`git merge-file`) |
| Eine Datei, bei der sich beide Änderungen in die Quere kommen | Deine Datei bleibt; `<datei>.rej` enthält die Zusammenführung mit Konfliktmarkern, die CLI listet sie auf |
| Eine Datei, die das Template nicht mehr hat | Gelöscht, wenn du sie nie angefasst hast, sonst aufgelistet |

Übernimm jede `.rej` von Hand in ihre Datei und lösche sie dann. Migrationen bleiben unberührt: Nach dem Zusammenführen stellt die CLI die Pakete wieder her und legt die Migration `Add<Option>` an (zum Beispiel `AddReports`), wenn sich das Datenbankmodell geändert hat, mit dem Provider der App. `--no-migration` lässt das aus, `--template-source` nimmt ein lokales `Coworkee.Templates`-Paket (eine App mit dem Feed `coworkee-local` in ihrer `nuget.config` findet es dort von selbst).

`coworkee remove` macht nur weiter, wenn sich jede betroffene Datei sauber zusammenführen lässt; sonst ändert es nichts und listet die Dateien auf. Wer ein Modul mit eigenen Tabellen entfernt, bekommt eine Migration, die sie samt Daten löscht.

Mit `--spec` werden Module und Entities der Spezifikation, die die App noch nicht hat, wie mit `coworkee module add` und `coworkee entity add` erzeugt; das nutzt Coworkee Studio für *Bestehende App öffnen*.

## Die lokale Datenbank

| Befehl | Was er tut |
|---|---|
| `coworkee db reset [--yes]` | Löscht die Datenbank: das Daten-Volume des App-Hosts (`<app>.apphost-<hash>-postgres-data` bzw. `-sqlserver-data`) oder die SQLite-Datei `.data/<name>.db`. Stoppe vorher den App-Host; der nächste Start migriert und befüllt sie neu |
| `coworkee db seed` | Führt das Migrationsprojekt gegen die laufende Datenbank aus: Migrationen, Demo-Benutzer und Rollen |
| `coworkee db backup [datei]` | `pg_dump` im laufenden Postgres-Container, `BACKUP DATABASE` mit `sqlcmd` im SQL-Server-Container oder eine Kopie der SQLite-Datei; standardmäßig nach `.data/backups/` |
| `coworkee db restore <datei> [--yes]` | Der umgekehrte Weg: `pg_restore --clean`, `RESTORE DATABASE ... WITH REPLACE` oder die Kopie zurück |

`reset` und `restore` fragen nach, bevor sie etwas löschen; `--yes` überspringt die Frage (Skripte). Apps, die aus mehreren Ordnern gestartet wurden, haben mehrere Volumes; wähle eines mit `--volume <name>`. Bei Postgres und SQL Server muss die App für `seed`, `backup` und `restore` laufen (`coworkee run`); bei SQLite und SQL Server stoppst du sie vor einem Restore.

## Coworkee Studio

```bash
coworkee ui
coworkee ui --spec shop.json
```

`coworkee ui` startet Coworkee Studio, einen Desktop-Assistenten für Name, Titel, Logo, Farben, Module, Anmeldedesign, Theme und Entities; er legt die App mit dieser CLI an. Das Studio ist das globale Tool `Coworkee.Studio` (Befehl `coworkee-ui`): `coworkee ui` installiert es in der Version der CLI, wenn es fehlt, und aktualisiert es, wenn es älter ist. Weitere Argumente gehen an das Studio, siehe [Coworkee Studio](studio.md). Für lokale Builds: `--prerelease`, `--add-source <ordner>` und `--studio-version <version>`; eine ausdrücklich genannte Version, die älter als die installierte ist, wird ebenfalls installiert. `--prerelease` nimmt die neueste Vorabversion statt der Version der CLI und entfällt neben `--studio-version`.

## Weitere Befehle

| Befehl | Was er tut |
|---|---|
| `coworkee run` | Startet das `*.AppHost`-Projekt der Solution |
| `coworkee migrations add <name>` | `dotnet ef migrations add <name>` mit dem `*.Infrastructure`-Projekt als Projekt und Startprojekt |
| `coworkee module add <name>` | Ein Feature-Modul mit seiner ersten Entity, siehe oben |
| `coworkee entity add <modul> <name>` | Eine weitere Entity in einem Modul, siehe oben |
| `coworkee texts export --lang <code> [--keys <glob>] [--out <datei>]` | Kopiert die Bibliothekstexte einer Sprache zum Ändern nach `src/<App>.Contracts/Localization/<code>.json` und führt sie mit dem Vorhandenen zusammen, siehe [Lokalisierung](../modules/localization.md#bibliothekstexte-andern) |
| `coworkee options [--json]` | Die Optionen von `coworkee new` |
| `coworkee spec schema`, `example`, `validate <datei>` | App-Spezifikationen |
| `coworkee add <option> [wert]`, `coworkee remove <option>` | Optionen einer bestehenden App, siehe oben |
| `coworkee db reset`, `seed`, `backup`, `restore` | Die lokale Datenbank, siehe oben |
| `coworkee spec current` | Die Optionen der App in diesem Ordner |
| `coworkee update [--version <version>] [--prerelease] [--dry-run]` | Setzt `<CoworkeeVersion>` in `Directory.Packages.props`, standardmäßig auf die neueste Version auf nuget.org, und wendet die Upgrade-Regeln an, siehe unten |
| `coworkee self-update [--version <version>] [--prerelease]` | Aktualisiert diese CLI und, falls installiert, Coworkee Studio auf dieselbe Version, siehe unten |
| `coworkee deploy [compose\|azure\|k8s] [-o <ordner>] [--no-build]` | Veröffentlicht den App-Host für Docker Compose (mit ausgefüllter `.env` und den Images), Azure Container Apps oder Kubernetes, siehe [Deployment](../hosting/deployment.md) |
| `coworkee doctor` | Prüft die Werkzeuge, siehe oben |
| `coworkee ui` | Coworkee Studio |
| `coworkee mcp` | [MCP-Server](mcp.md) über stdio |

`coworkee update` setzt nicht nur die Version. Die CLI bringt die Upgrade-Regeln jeder Coworkee-Version mit (`upgrade-rules.json`, aus dem [Changelog](../reference/changelog.md)): Für jede Version zwischen der der App und der neuen zeigt sie die Breaking Changes, erledigt, was sich automatisch erledigen lässt (Umbenennungen wie `PostgresFixture` zu `DatabaseFixture`, Paket- und SDK-Versionen, die älter sind, als die Version braucht, die Zeile für den Ladebildschirm in `App.razor`), und listet die Handarbeit mit jeder betroffenen Stelle als `datei:zeile` auf. `--dry-run` zeigt all das und ändert nichts. Danach führst du `dotnet restore` aus; die CLI sagt dir, wenn sich das Modell der Bibliothek geändert hat und eine Migration fällig ist (`coworkee migrations add CoworkeeUpdate`).

`coworkee self-update` aktualisiert die Werkzeuge selbst: `dotnet tool update -g Coworkee.Cli` und, wenn das Studio installiert ist, `dotnet tool update -g Coworkee.Studio`, beide auf die neueste Version auf nuget.org oder auf `--version`. Eine Vorabversion der CLI berücksichtigt auch Vorabversionen; bei einem Release tut das `--prerelease`. Unter Windows kann sich die laufende `coworkee.exe` nicht selbst ersetzen, deshalb läuft das Update in einem eigenen Fenster, sobald der Befehl beendet ist.

## Ausgabe für Werkzeuge

`coworkee new` schreibt vor jedem Schritt eine Zeile `==> <schritt>` (Template installieren, Solution anlegen, Modul erzeugen, Restore, Migration anlegen, Git), vor jedem Befehl, den es ausführt, `> <befehl>`, und endet mit einem Code ungleich 0, wenn ein Schritt scheitert. Probleme mit der Eingabe stehen einzeln pro Zeile auf stderr.

## Ohne das Tool

Das Template funktioniert auch mit reinem `dotnet new`; die Schalter heißen wie die Ids der Tabelle (`--eventBus`, `--primaryColor`, ...):

```bash
dotnet new install Coworkee.Templates
dotnet new coworkee -n Shop --samples false --keycloak true --title "My Shop" --theme Sunset
dotnet run --project Shop/src/Shop.AppHost
```

`dotnet new coworkee --help` listet sie auf. Kopiere ein Logo selbst nach `src/<Name>.Web.Client/wwwroot`, bevor du `--logo <dateiname>` angeben.
