namespace Coworkee.Cli.Mcp;

/// <summary>Output and exit code of a coworkee command the MCP server ran.</summary>
internal sealed record CliRun(int ExitCode, string Output)
{
    public override string ToString() => $"exit code {ExitCode}\n{Output}";
}
