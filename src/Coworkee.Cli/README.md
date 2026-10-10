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
| `coworkee new <name> [switches]` | Installs `Coworkee.Templates` of the tool's version, creates the solution, restores it and adds the migration the chosen modules need. `coworkee options` lists every switch: modules, event bus, storage, client entities, Keycloak, theme, brand colors, logo, sign-in layout and media, DTO mapping, git |
| `coworkee new --spec app.json` | The same from a JSON spec with name, options, branding and feature modules with entities and properties |
| `coworkee options [--json]` | Every switch with default, choices, pros, cons and when to use it |
| `coworkee spec schema`, `spec example`, `spec validate <file>` | JSON Schema, an example and the check of a spec |
| `coworkee run` | Runs the `*.AppHost` project of the solution |
| `coworkee migrations add <name>` | `dotnet ef migrations add` against the `*.Infrastructure` project |
| `coworkee module add <name> [--entity <name>] [--property Name:type:required:max]...` | Feature module with an entity: table, permissions, OData set, endpoints, validation, Blazor page and navigation |
| `coworkee entity add <module> <name> [--property ...]` | Another entity in a generated module |
| `coworkee update [--version <version>] [--prerelease]` | Sets `CoworkeeVersion` in `Directory.Packages.props` (default: newest on nuget.org) |
| `coworkee doctor` | Checks the .NET SDK, Docker and dotnet-ef |
| `coworkee ui [args]` | Installs or updates the global tool `Coworkee.Studio` to the CLI version and starts the desktop wizard (`coworkee-ui`) |
| `coworkee mcp` | MCP server over stdio for AI agents: `claude mcp add coworkee -- coworkee mcp` |

Docs: https://fgilde.github.io/CoworkeeLib/getting-started/cli/ and https://fgilde.github.io/CoworkeeLib/getting-started/mcp/
