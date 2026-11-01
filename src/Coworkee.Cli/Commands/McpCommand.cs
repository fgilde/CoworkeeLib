using System.CommandLine;
using System.Reflection;
using Coworkee.Cli.Mcp;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace Coworkee.Cli.Commands;

internal static class McpCommand
{
    public const string Description = "Run an MCP server over stdio, so AI agents can create and extend Coworkee apps (coworkee mcp --help shows how to register it).";

    /// <summary>The tools as the server lists them, from their attributes.</summary>
    public static IReadOnlyList<string> Tools { get; } = typeof(CoworkeeMcpTools).GetMethods()
        .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name).OfType<string>().ToList();

    public static string Help => $"Tools: {string.Join(", ", Tools)}.{Environment.NewLine}{Environment.NewLine}" + Registration;

    private const string Registration = """
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
