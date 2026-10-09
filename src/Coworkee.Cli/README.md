# Coworkee.Cli

The `coworkee` command: create a Coworkee application and keep it running.

```bash
dotnet tool install -g Coworkee.Cli
coworkee new Shop
cd Shop
coworkee run
```

| Command | |
|---|---|
| `coworkee new <name> [-o <dir>] [--no-samples] [--keycloak] [--no-tests] [--title <title>] [--template-source <nupkg or folder>]` | Installs `Coworkee.Templates` of the tool's version, creates the solution and restores it |
| `coworkee run` | Runs the `*.AppHost` project of the solution |
| `coworkee migrations add <name>` | `dotnet ef migrations add` against the `*.Infrastructure` project |
| `coworkee module add <name> [--entity <name>]` | New feature module with an entity, permissions, an OData set and a create endpoint, registered in the solution |
| `coworkee update [--version <version>] [--prerelease]` | Sets `CoworkeeVersion` in `Directory.Packages.props` (default: newest on nuget.org) |
| `coworkee doctor` | Checks the .NET SDK, Docker and dotnet-ef |

Docs: https://fgilde.github.io/CoworkeeLib/getting-started/cli/
