namespace Coworkee.Contracts.Ai;

/// <summary>A tool call waiting for the user; <see cref="Input"/> has secret looking values masked.</summary>
public sealed record AiPendingToolDto(string Tool, string Input, bool Destructive = true);
