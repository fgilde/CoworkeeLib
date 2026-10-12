namespace Coworkee.Contracts.Ai;

public static class AiSettingNames
{
    public const string Enabled = "Ai.Enabled";
    public const string Provider = "Ai.Provider";
    public const string Model = "Ai.Model";
    public const string Endpoint = "Ai.Endpoint";
    public const string ApiKey = "Ai.ApiKey";
    public const string Headers = "Ai.Headers";
    public const string AuthHeader = "Ai.AuthHeader";
    public const string Temperature = "Ai.Temperature";
    public const string MaxOutputTokens = "Ai.MaxOutputTokens";
    public const string MaxToolRounds = "Ai.MaxToolRounds";
}

/// <summary>
/// A connection as the AI page edits it, before it is saved. <see cref="ApiKey"/> null takes the stored key while provider and endpoint stay
/// the same, empty sends none. <see cref="Headers"/> are lines "Name: value"; <see cref="AuthHeader"/> sends the key in that header instead of a bearer token.
/// </summary>
public sealed record AiConnectionDraft(
    string Provider, string? Model = null, string? Endpoint = null, string? ApiKey = null, string? Headers = null, string? AuthHeader = null);

public sealed record AiModelDto(string Id, string? Name = null);

/// <summary>The models a provider lists for a key, or why it listed none.</summary>
public sealed record AiModelListDto(IReadOnlyList<AiModelDto> Models, string? Error = null);

/// <summary>Whether the assistant answers now; <see cref="HasKey"/> counts a stored key and one from the configuration (user secrets).</summary>
public sealed record AiConnectionStatusDto(bool Enabled, bool Active, bool HasKey, string Provider, string? Model);

/// <summary>Result of a short prompt and of a tool call probe against a connection.</summary>
public sealed record AiConnectionTestDto(bool Succeeded, int LatencyMs, string? Reply, bool CallsTools, string? Error);
