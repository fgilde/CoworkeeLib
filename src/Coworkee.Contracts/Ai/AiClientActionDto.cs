using System.Text.Json;

namespace Coworkee.Contracts.Ai;

/// <summary>A tool the client runs itself (navigate, switch the theme...); <see cref="Parameters"/> is the JSON schema of its arguments.</summary>
public sealed record AiClientActionDto(string Name, string Description, JsonElement? Parameters = null);
