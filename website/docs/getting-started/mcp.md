# MCP server of the CLI

`coworkee mcp` runs a [Model Context Protocol](https://modelcontextprotocol.io) server over stdio. An AI agent such as Claude Code or Claude Desktop can then plan an app with you, write its spec, create it and add modules and entities, with the same commands you would type.

This is the MCP server of the command line, on your machine. A generated app has its own MCP endpoint under `/mcp`, with the tools of the app, see [AI assistant and MCP](../modules/ai.md).

## Register it

The CLI must be installed as global tool (`dotnet tool install -g Coworkee.Cli`).

=== "Claude Code"

    ```bash
    claude mcp add coworkee -- coworkee mcp
    ```

=== "Claude Desktop"

    ```json title="claude_desktop_config.json"
    {
      "mcpServers": {
        "coworkee": { "command": "coworkee", "args": ["mcp"] }
      }
    }
    ```

=== "VS Code"

    ```json title=".vscode/mcp.json"
    {
      "servers": {
        "coworkee": { "type": "stdio", "command": "coworkee", "args": ["mcp"] }
      }
    }
    ```

Other clients take the same command: `coworkee` with the argument `mcp`. `coworkee mcp --help` prints these snippets.

## Tools

| Tool | What it does |
|---|---|
| `list_options` | The [options](cli.md#options) with id, kind, default, choices, pros, cons and when to use them; optionally one group |
| `get_spec_schema` | JSON Schema of an [app spec](cli.md#app-specs) |
| `get_spec_example` | A complete example spec |
| `validate_spec` | Checks a spec, with a folder also its logo and media files |
| `create_app` | `coworkee new` from a spec, or from a name and options, in a folder |
| `add_module` | `coworkee module add` with entity, properties, client entity and mapping |
| `add_entity` | `coworkee entity add` |
| `add_migration` | `coworkee migrations add` |
| `run_doctor` | `coworkee doctor` |
| `update_packages` | `coworkee update` |
| `search_docs` | Searches this documentation (the published search index; offline the page titles) |

The tools that change something run the CLI as a child process in the given folder and answer with its exit code and output, so they behave exactly like the commands. Creating an app takes minutes: the template, the restore and, when needed, the first migration.

## A session

> Create a shop app in C:\work: no samples, no chat, with files and the Forest theme in our green #0a7d5a. It needs customers (name, email) and orders (number, total, order date) that users filter all day.

The agent reads `list_options` and `get_spec_schema`, writes a spec with a `Sales` module, marks `Order` as client entity, checks it with `validate_spec` and calls `create_app`. Afterwards `add_entity` and `add_migration` extend the app the same way.

The server introduces itself to the agent with this order of work, so a plain request is enough.
