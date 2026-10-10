# Command line

The `coworkee` tool creates a Coworkee application from the `Coworkee.Templates` package, with the modules, branding and entities you choose, and takes care of the everyday commands: run, migrations, new modules and entities, package updates. It also runs an [MCP server](mcp.md) for AI agents and opens Coworkee Studio, a desktop wizard.

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

`coworkee new` installs `Coworkee.Templates` in the version of the tool, creates the solution, generates the modules of a spec, restores the packages and adds the migration the chosen modules need. The generated app references the Coworkee packages of that same version. The name must be simple: a letter, then letters or digits (`Shop`, `Crm2`). It becomes the prefix of every project and namespace.

The migration worker seeds an administrator `admin@<name>.local`. Its password was generated when the solution was created and is in `src/<Name>.Migrations/DemoSeed.cs`; change it before you deploy anywhere.

## Options

Every option is a switch of `coworkee new` and, under its id, a key in the `options` of a [spec](#app-specs). `coworkee options` prints them with the reasons to choose them, `coworkee options --json` as data.

```bash
coworkee new Shop --no-samples --chat false --files --event-bus rabbitMq \
  --theme Forest --primary-color "#0a7d5a" --logo ./brand/logo.svg \
  --sign-in-layout Split --sign-in-media ./brand/welcome.mp4 --git
```

| Switch | Id | Values | Default | |
|---|---|---|---|---|
| `-o`, `--output` | | folder | `./<name>` | Target folder |
| `--spec` | | file | | Create the app from a spec, see below; switches win over the spec |
| `--template-source` | | file or folder | nuget.org | Install the template from a `.nupkg` or a folder, for offline use or a local build |
| `--title` | `title` | text | the name | Display title in the browser, the app bar, the sign-in pages and the Aspire dashboard |
| `--samples`, `--no-samples` | `samples` | bool | `true` | Catalog (products, brands, dashboard) and Documents modules with pages, tests and demo data |
| `--tests`, `--no-tests` | `tests` | bool | `true` | API, migration, app host and page tests |
| `--git` | `git` | bool | `false` | `git init` in the new folder |
| `--auditing` | `auditing` | bool | `true` | Audit log pages, entity history and restore; the trail itself is always written |
| `--notifications` | `notifications` | bool | `true` | In-app notifications with mail digests |
| `--chat` | `chat` | bool | `true` | Direct and group chat |
| `--ai` | `ai` | bool | `true` | AI assistant, MCP endpoint of the app and tool call audit |
| `--backup` | `backup` | bool | `true` | Database backups from the admin pages |
| `--files` | `files` | bool | `false` | Folders and files with preview and per-folder permissions |
| `--features` | `features` | bool | `false` | Features, editions and the tenant administration |
| `--social` | `social` | bool | `false` | Comments, tags and ratings |
| `--extended-attributes` | `extendedAttributes` | bool | `false` | Key/value attributes for any entity (the Documents sample brings it anyway) |
| `--elasticsearch` | `elasticsearch` | bool | `false` | Elasticsearch index; the app host adds the container |
| `--event-bus` | `eventBus` | `none`, `inProcess`, `rabbitMq` | `none` | Integration events through the outbox; `rabbitMq` adds the transport and a RabbitMQ container |
| `--client-entities` | `clientEntities` | bool | `false` | Browser copies of OData sets marked `[ClientEntity]`; spec entities with `clientEntity` switch it on |
| `--keycloak` | `keycloak` | bool | `false` | Keycloak container as external sign-in |
| `--storage` | `storage` | `fileSystem`, `azureBlob` | `fileSystem` | Blob storage; `azureBlob` runs Azurite in the app host |
| `--theme` | `theme` | `Ocean`, `Coworkee`, `Classic`, `Forest`, `Midnight`, `Sunset`, `HighContrast` | `Ocean` | Default theme |
| `--primary-color` | `primaryColor` | `#rrggbb` | | Brand color: the app gets its own theme, based on `--theme` and named like the title, as default |
| `--secondary-color` | `secondaryColor` | `#rrggbb` | | Second brand color |
| `--logo` | `logo` | file | Coworkee icon | Copied into the web client; app bar, browser tab and sign-in pages |
| `--sign-in-layout` | `signInLayout` | `Card`, `Split`, `Slide` | `Slide` | Layout of the [sign-in pages](../security/sign-in-pages.md) |
| `--sign-in-media` | `signInMedia` | file or http(s) address | | Image or video next to the sign-in form; a file is copied to `wwwroot/media` of the web client |
| `--sign-in-media-side` | `signInMediaSide` | `Left`, `Right` | `Left` | Side of the media |
| `--mapping` | `mapping` | `manual`, `mapperly` | `manual` | DTO mapping of generated entities, see [DTOs and mapping](../fundamentals/dtos.md) |

A boolean switch without value means `true` (`--files`), `--chat false` switches off. The shipped initial migration covers the default modules (with or without samples). When you switch a module with tables on or off (chat, AI, backup, notifications, files, features, social, event bus, extended attributes without samples), the template leaves the migration out and `coworkee new` creates `Initial` with `dotnet ef` after the restore. With plain `dotnet new`, run `coworkee migrations add Initial` once yourself.

## App specs

A spec describes the whole app as JSON: name, options, branding and feature modules with their entities. It is the format of Coworkee Studio and of the [MCP server](mcp.md).

```bash
coworkee spec example > shop.json    # a complete example
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

- `options` takes the ids of the table above; `branding` takes `title`, `theme`, `primaryColor`, `secondaryColor`, `logo`, `signInLayout`, `signInMedia` and `signInMediaSide` and wins over the same ids in `options`.
- Files and `output` are relative to the spec file and must stay inside its folder (no absolute paths, no `..`). A logo is an image (svg, png, jpg, jpeg, webp, gif, ico), sign-in media an image, a video (mp4, webm, ogg) or an http(s) address: both end up public in the web client. A file given as switch (`--logo C:\brand\logo.svg`) may be anywhere. Comments and trailing commas are allowed.
- The title goes into code and HTML as it is, so it must not contain `"`, `\`, `<`, `>`, `@` or control characters.
- Property types: `string`, `int`, `long`, `decimal`, `double`, `bool`, `dateTime` (`DateTimeOffset`), `guid`. A property that is not `required` is nullable; `maxLength` is for strings.
- `plural` names table, OData set, route and page (default: an English plural of the name). `mapping` overrides the `mapping` option.

`coworkee spec validate` and `coworkee new` report every problem with its path, for example `modules[0].entities[1].properties[0].name: 'Id' is taken by the entity itself.`

## Modules and entities

Each module of a spec, and `coworkee module add`, creates the project `src/<App>.<Module>` the way [Your first feature](first-feature.md) builds the brands. Per entity:

| Where | What |
|---|---|
| contracts | `<Entity>Dto`, `AddEdit<Entity>Request`, `<Module>Permissions.<Plural>` (view, create, edit, delete) |
| module | entity (`AuditedEntity`, `IMultiTenant`, `[Realtime]`, `[ClientEntity]` when asked), table with maximum lengths, permission definitions, OData set, add/edit command with permission check and validator, delete command, mapping (`ToDto` method or Mapperly mapper), endpoints `POST`, `PUT /{id}` and `POST /delete` under `/api/v1/<plural>` |
| web client | page `/<module>/<plural>` with data table, create, edit and delete, live reload, API client and a navigation entry in the group of the module |

The module goes into the `.slnx`, the infrastructure project references it and `<App>DatabaseModule` depends on it; `Program.cs` of the web client registers the API clients and navigation entries.

```bash
coworkee module add Sales --entity Order --property Number:string:required:50 --property Total:decimal --client-entity
coworkee entity add Sales Invoice -p Number:required:30 -p DueAt:dateTime -p Paid:bool --mapping Mapperly
coworkee migrations add AddSales
```

`--property` reads `Name[:type][:required][:maxLength]` (default: `Name:string:required:200`). `entity add` needs a module created by the CLI.

## Coworkee Studio

```bash
coworkee ui
coworkee ui --spec shop.json
```

`coworkee ui` starts Coworkee Studio, a desktop wizard for name, title, logo, colors, modules, sign-in design, theme and entities; it creates the app with this CLI. The Studio is the global tool `Coworkee.Studio` (command `coworkee-ui`): `coworkee ui` installs it in the version of the CLI when it is missing and updates it when it is older. Other arguments go to the Studio. For local builds: `--prerelease`, `--add-source <folder>` and `--studio-version <version>`; an explicit version older than the installed one is installed as well. `--prerelease` takes the newest prerelease instead of the CLI's version and is ignored next to `--studio-version`.

## Other commands

| Command | What it does |
|---|---|
| `coworkee run` | Runs the `*.AppHost` project of the solution |
| `coworkee migrations add <name>` | `dotnet ef migrations add <name>` with the `*.Infrastructure` project as project and startup project |
| `coworkee module add <name>` | A feature module with its first entity, see above |
| `coworkee entity add <module> <name>` | Another entity in a module, see above |
| `coworkee options [--json]` | The options of `coworkee new` |
| `coworkee spec schema`, `example`, `validate <file>` | App specs |
| `coworkee update [--version <version>] [--prerelease]` | Sets `<CoworkeeVersion>` in `Directory.Packages.props`, by default to the newest version on nuget.org |
| `coworkee doctor` | Checks the tools, see above |
| `coworkee ui` | Coworkee Studio |
| `coworkee mcp` | [MCP server](mcp.md) over stdio |

After `coworkee update`, run `dotnet restore` and add a migration if a Coworkee module changed its model (`coworkee migrations add CoworkeeUpdate`).

## Output for tools

`coworkee new` prints a line `==> <step>` before each step (installing the template, creating the solution, generating a module, restoring, adding the migration, git), `> <command>` before each command it runs, and exits with a code other than 0 when a step fails. Problems with the input are printed one per line on stderr.

## Without the tool

The template works with plain `dotnet new` as well; the switches have the ids of the table (`--eventBus`, `--primaryColor`, ...):

```bash
dotnet new install Coworkee.Templates
dotnet new coworkee -n Shop --samples false --keycloak true --title "My Shop" --theme Sunset
dotnet run --project Shop/src/Shop.AppHost
```

`dotnet new coworkee --help` lists them. Copy a logo into `src/<Name>.Web.Client/wwwroot` yourself before you pass `--logo <file name>`.
