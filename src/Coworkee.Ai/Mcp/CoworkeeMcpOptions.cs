namespace Coworkee.Ai.Mcp;

/// <summary>The MCP server of the app (section Coworkee:Mcp): on by default under /mcp, for users and API keys with Ai.Chat.</summary>
public sealed class CoworkeeMcpOptions
{
    public const string Section = "Coworkee:Mcp";

    public bool Enabled { get; set; } = true;

    public string Path { get; set; } = "/mcp";
}
