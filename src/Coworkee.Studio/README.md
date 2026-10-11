# Coworkee.Studio

Coworkee Studio is the desktop wizard of the `coworkee` CLI for Windows, macOS and Linux. It walks through a new Coworkee application, shows what every choice brings and costs, and lets the CLI create it.

```bash
dotnet tool install -g Coworkee.Cli
coworkee ui
```

`coworkee ui` installs this tool (command `coworkee-ui`) in the version of the CLI when it is missing, updates it when it is older, and starts it. Further arguments go to the Studio, for example `coworkee ui --spec shop.json`.

| Page | |
|---|---|
| Basics | Name (prefix of every project and namespace), title and folder |
| Branding | Logo, the seven built-in themes, brand colors, with a live preview of the app shell in light and dark |
| Sign-in | Card, Split or Slide, image or video and its side |
| Architecture | Presets for the app type, then every module and infrastructure option with description, pros, cons, when to use it and its package; the projects, Aspire containers and packages of the solution |
| Entities | Feature modules with entities and properties (references, enums, flags), enums, plural, client entity and mapping; routes, permissions and files the CLI generates |
| Review | Validation, the app spec as JSON and the equivalent `coworkee new` command |
| Create | Runs `coworkee spec validate` and `coworkee new --spec`, shows the progress, then opens the folder, VS Code, Rider or Visual Studio, or runs the app |

The help (F1) explains the wizard, the CLI commands and the MCP server `coworkee mcp` with the registration for Claude Code, Claude Desktop and VS Code.

| Argument | |
|---|---|
| `--spec <file>` | Open an app spec |
| `--output <folder>` | Folder in which the app folder is created |
| `--theme light\|dark` | Color mode (default: the system's) |
| `--lang en\|de` | Language (default: the system's) |

Settings (recent specs, CLI path, template source, local package feed, language) are kept per user in the application data folder under `Coworkee/Studio`.

Documentation: https://fgilde.github.io/CoworkeeLib/getting-started/studio/
