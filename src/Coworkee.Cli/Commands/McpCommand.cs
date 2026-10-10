using System.CommandLine;
using Coworkee.Cli.Mcp;
using Microsoft.Extensions.Hosting;

namespace Coworkee.Cli.Commands;

internal static class McpCommand
{
    public const string Description = "Run an MCP server over stdio, so AI agents can create and extend Coworkee apps (coworkee mcp --help shows how to register it).";

    public const string Help = """
        Tools: list_options, get_spec_schema, get_spec_example, validate_spec, create_app, add_module, add_entity,
        add_migration, run_doctor, update_packages, search_docs.

        Claude Code:
          claude mcp add coworkee -- coworkee mcp

        Claude Desktop (claude_desktop_config.json) and other clients with mcpServers:
          { "mcpServers": { "coworkee": { "command": "coworkee", "args": ["mcp"] } } }

        VS Code (.vscode/mcp.json):
          { "servers": { "coworkee": { "type": "stdio", "command": "coworkee", "args": ["mcp"] } } }

        Docs: https://fgilde.github.io/CoworkeeLib/getting-started/mcp/
        """;

    public static Command Create() => new Command("mcp", Description).Handle(async (_, cancellationToken) =>
    {
        await McpServerHost.Build(McpServerHost.RunSelfAsync).RunAsync(cancellationToken);
        return 0;
    });
}
