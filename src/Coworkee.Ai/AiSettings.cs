using Coworkee.Contracts.Ai;

namespace Coworkee.Ai;

public static class AiSettings
{
    public const string Enabled = AiSettingNames.Enabled;
    public const string Provider = AiSettingNames.Provider;
    public const string Model = AiSettingNames.Model;
    public const string Endpoint = AiSettingNames.Endpoint;
    public const string ApiKey = AiSettingNames.ApiKey;
    public const string Headers = AiSettingNames.Headers;
    public const string AuthHeader = AiSettingNames.AuthHeader;
    public const string Temperature = AiSettingNames.Temperature;
    public const string MaxOutputTokens = AiSettingNames.MaxOutputTokens;
    public const string MaxToolRounds = AiSettingNames.MaxToolRounds;
    public const string DefaultModel = "claude-opus-5-5";

    /// <summary>The kill switch of the MCP server at runtime; Coworkee:Mcp:Enabled decides whether it is mapped at all.</summary>
    public const string McpEnabled = AiSettingNames.McpEnabled;
}
