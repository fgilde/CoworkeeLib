namespace Coworkee.Cli.Mcp;

/// <summary>Runs this CLI with arguments in a folder; the MCP server never writes command output to its own stdout, which carries the protocol.</summary>
internal delegate Task<CliRun> CliRunner(IReadOnlyList<string> args, string workingDirectory, CancellationToken cancellationToken);
