# Kommandozeile

Das Tool `coworkee` legt eine Coworkee-Anwendung aus dem Paket `Coworkee.Templates` an und übernimmt die alltäglichen Befehle: starten, Migrationen, neue Module, Paket-Updates.

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

`coworkee new` installiert `Coworkee.Templates` in der Version des Tools, legt die Solution an und stellt die Pakete wieder her. Die erzeugte App verweist auf die Coworkee-Pakete genau dieser Version.

| Option | Standard | |
|---|---|---|
| `-o`, `--output <ordner>` | `./<name>` | Zielordner |
| `--samples`, `--no-samples` | mit Beispielen | Module Catalog (Produkte, Marken, Dashboard) und Documents mit ihren Seiten |
| `--keycloak` | aus | Keycloak-Container als externe Anmeldung |
| `--no-tests` | mit Tests | API-, Migrations-, App-Host- und Seitentests |
| `--title <titel>` | der Name | Anzeigetitel im Browser, in der App-Leiste und im Aspire-Dashboard |
| `--template-source <pfad>` | nuget.org | Template aus einer `.nupkg` oder einem Ordner installieren, für Offline-Betrieb oder einen lokalen Build |

Der Name muss einfach sein: ein Buchstabe, dann Buchstaben oder Ziffern (`Shop`, `Crm2`). Er wird zum Präfix aller Projekte und Namespaces.

Der Migrations-Worker legt einen Administrator `admin@<name>.local` an. Sein Passwort wurde beim Anlegen der Solution erzeugt und steht in `src/<Name>.Migrations/DemoSeed.cs`; ändern Sie es, bevor Sie irgendwo deployen.

## Weitere Befehle

| Befehl | Was er tut |
|---|---|
| `coworkee run` | Startet das Projekt `*.AppHost` der Solution |
| `coworkee migrations add <name>` | `dotnet ef migrations add <name>` mit dem Projekt `*.Infrastructure` als Projekt und Startprojekt |
| `coworkee module add <name> [--entity <name>]` | Legt `src/<App>.<Name>` an: Modulklasse, eine Entität mit Tabelle, Berechtigungen, ein OData-Set und einen Endpunkt zum Anlegen, dazu DTO und Berechtigungsnamen im Contracts-Projekt. Das Projekt kommt in die `.slnx`, das Infrastructure-Projekt verweist darauf und `<App>DatabaseModule` registriert das Modul |
| `coworkee update [--version <version>] [--prerelease]` | Setzt `<CoworkeeVersion>` in `Directory.Packages.props`, standardmäßig auf die neueste Version auf nuget.org |
| `coworkee doctor` | Prüft die Werkzeuge, siehe oben |

Ein neues Modul braucht eine Migration für seine Tabelle:

```bash
coworkee module add Orders
coworkee migrations add AddOrders
```

Nach `coworkee update` führen Sie `dotnet restore` aus und legen eine Migration an, falls ein Coworkee-Modul sein Modell geändert hat (`coworkee migrations add CoworkeeUpdate`).

## Ohne das Tool

Das Template funktioniert auch mit reinem `dotnet new`:

```bash
dotnet new install Coworkee.Templates
dotnet new coworkee -n Shop --samples false --keycloak true --title "Mein Shop"
dotnet run --project Shop/src/Shop.AppHost
```

`dotnet new coworkee --help` listet die Optionen (`--samples`, `--keycloak`, `--tests`, `--title`).
