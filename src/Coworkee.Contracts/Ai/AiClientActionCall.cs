namespace Coworkee.Contracts.Ai;

/// <summary>A client action the assistant asked for; <see cref="Arguments"/> is a JSON object.</summary>
public sealed record AiClientActionCall(string CallId, string Name, string Arguments);
