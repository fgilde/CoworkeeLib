# Command line

The `coworkee` tool creates a Coworkee application from the `Coworkee.Templates` package and takes care of the everyday commands: run, migrations, new modules, package updates.

## Install

```bash
dotnet tool install -g Coworkee.Cli
coworkee doctor
```

`coworkee doctor` checks the .NET SDK (at least the version in `global.json`), whether Docker runs and whether `dotnet-ef` is installed. The Aspire CLI is optional; `coworkee run` uses `dotnet run`.

## A new application

```bash
coworkee new Shop
cd Shop
coworkee run
```

`coworkee new` installs `Coworkee.Templates` in the version of the tool, creates the solution and restores it. The generated app references the Coworkee packages of that same version.

| Option | Default | |
|---|---|---|
| `-o`, `--output <dir>` | `./<name>` | Target folder |
| `--samples`, `--no-samples` | with samples | Catalog (products, brands, dashboard) and Documents modules with their pages |
| `--keycloak` | off | Keycloak container as external sign-in |
| `--no-tests` | with tests | API, migration, app host and page tests |
| `--title <title>` | the name | Display title in the browser, the app bar and the Aspire dashboard |
| `--template-source <path>` | nuget.org | Install the template from a `.nupkg` or a folder, for offline use or a local build |

The name must be simple: a letter, then letters or digits (`Shop`, `Crm2`). It becomes the prefix of every project and namespace.

The migration worker seeds an administrator `admin@<name>.local`. Its password was generated when the solution was created and is in `src/<Name>.Migrations/DemoSeed.cs`; change it before you deploy anywhere.

## Other commands

| Command | What it does |
|---|---|
| `coworkee run` | Runs the `*.AppHost` project of the solution |
| `coworkee migrations add <name>` | `dotnet ef migrations add <name>` with the `*.Infrastructure` project as project and startup project |
| `coworkee module add <name> [--entity <name>]` | Creates `src/<App>.<Name>` with a module class, an entity, its table, permissions, an OData set and a create endpoint, plus DTO and permission names in the contracts project. It adds the project to the `.slnx`, references it from the infrastructure project and registers the module in `<App>DatabaseModule` |
| `coworkee update [--version <version>] [--prerelease]` | Sets `<CoworkeeVersion>` in `Directory.Packages.props`, by default to the newest version on nuget.org |
| `coworkee doctor` | Checks the tools, see above |

A new module needs a migration for its table:

```bash
coworkee module add Orders
coworkee migrations add AddOrders
```

After `coworkee update`, run `dotnet restore` and add a migration if a Coworkee module changed its model (`coworkee migrations add CoworkeeUpdate`).

## Without the tool

The template works with plain `dotnet new` as well:

```bash
dotnet new install Coworkee.Templates
dotnet new coworkee -n Shop --samples false --keycloak true --title "My Shop"
dotnet run --project Shop/src/Shop.AppHost
```

`dotnet new coworkee --help` lists the options (`--samples`, `--keycloak`, `--tests`, `--title`).
