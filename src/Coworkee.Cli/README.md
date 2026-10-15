# Coworkee.Cli

The `coworkee` command: create a Coworkee application with the modules, branding and entities you choose, and keep it running.

```bash
dotnet tool install -g Coworkee.Cli
coworkee new Shop --no-samples --chat false --files --theme Forest --primary-color "#0a7d5a" --logo ./logo.svg
cd Shop
coworkee run
```

| Command | |
|---|---|
| `coworkee new <name> [switches]` | Installs `Coworkee.Templates` of the tool's version, creates the solution, restores it and adds the migration the chosen modules need. `coworkee options` lists every switch: modules, event bus, storage, client entities, Keycloak, theme, brand colors, logo, sign-in layout and media, DTO mapping, languages, color mode, registration, CI workflow, dev container, git |
| `coworkee new --spec app.json` | The same from a JSON spec with name, options, branding and feature modules with entities and properties |
| `coworkee options [--json]` | Every switch with default, choices, pros, cons and when to use it |
| `coworkee spec schema`, `spec example`, `spec validate <file>` | JSON Schema, an example and the check of a spec |
| `coworkee specs list`, `specs show <id>`, `new <name> --template-spec <id>` | The starter specs that come with the tool (CRM, helpdesk, asset portal, shop, events, approvals) and an app made from one |
| `coworkee run` | Runs the `*.AppHost` project of the solution |
| `coworkee migrations add <name>` | `dotnet ef migrations add` against the `*.Infrastructure` project |
| `coworkee module add <name> [--entity <name>] [--property Name:type:required:max:key=value]... [--enum Name=A,B]` | Feature module with an entity (references, enums, money, dates, email, url, unique, facets): table, permissions, OData set, endpoints, validation, Blazor page and navigation |
| `coworkee entity add <module> <name> [--property ...]` | Another entity in a generated module |
| `coworkee texts export --lang <code> [--keys <glob>] [--out <file>]` | Copies the library texts of a language (23 ship) into `src/<App>.Contracts/Localization/<code>.json` so you can change them; merges, never overwrites existing texts |
| `coworkee add <option> [value] [--dry-run]`, `coworkee remove <option>` | Turns options of an existing app on or off: the template's changes are merged into your files (conflicts as `.rej`), then a migration |
| `coworkee db reset\|seed\|backup\|restore` | The local development database of the app host (volume or SQLite file) |
| `coworkee update [--version <version>] [--prerelease] [--dry-run]` | Sets `CoworkeeVersion` in `Directory.Packages.props` (default: newest on nuget.org), applies the upgrade rules and lists breaking changes and manual steps |
| `coworkee doctor` | Checks the .NET SDK, Docker and dotnet-ef |
| `coworkee ui [args]` | Installs or updates the global tool `Coworkee.Studio` to the CLI version and starts the desktop wizard (`coworkee-ui`) |
| `coworkee mcp` | MCP server over stdio for AI agents: `claude mcp add coworkee -- coworkee mcp` |

Docs: https://fgilde.github.io/CoworkeeLib/getting-started/cli/ and https://fgilde.github.io/CoworkeeLib/getting-started/mcp/
