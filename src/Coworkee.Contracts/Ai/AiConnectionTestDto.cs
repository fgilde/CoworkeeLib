namespace Coworkee.Contracts.Ai;

/// <summary>Result of a short prompt and of a tool call probe against a connection.</summary>
public sealed record AiConnectionTestDto(bool Succeeded, int LatencyMs, string? Reply, bool CallsTools, string? Error);
