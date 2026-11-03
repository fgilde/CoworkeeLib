namespace Coworkee.Contracts.Ai;

/// <summary>Whether the assistant answers now; <see cref="HasKey"/> counts a stored key and one from the configuration (user secrets).</summary>
public sealed record AiConnectionStatusDto(bool Enabled, bool Active, bool HasKey, string Provider, string? Model);
