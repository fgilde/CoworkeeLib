namespace Coworkee.Contracts.Ai;

/// <summary>Whether the assistant can answer the current user: switched on, configured and permitted.</summary>
public sealed record AiStatusDto(bool Available);
